using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RatCache.Web.Data;
using RatCache.Web.Models;

namespace RatCache.Web.Services;

public record ApiTokenInfo(int Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

// Token is the secret the CLI sends as "Authorization: Bearer <token>". It's only available here.
public record NewApiToken(ApiTokenInfo Info, string Token);

// FR-1.8: create, list and revoke CLI API tokens. NFR-2.6: only SHA-256 hashes are stored.
// Tokens are 256 random bits, so a fast hash is fine (no bcrypt needed).
public class ApiTokenService(RatCacheDbContext db, TimeProvider time)
{
    public const string Prefix = "rc_";
    public const int NameMaxLength = 64;

    public async Task<ServiceResult<NewApiToken>> CreateAsync(int userId, string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "cli" : name.Trim();
        if (name.Length > NameMaxLength)
            return new ServiceError(ErrorKind.Invalid, "invalid_token_name",
                $"Token names can be at most {NameMaxLength} characters.");

        var token = Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var entity = new ApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = Hash(token),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.ApiTokens.Add(entity);
        await db.SaveChangesAsync();
        return new NewApiToken(ToInfo(entity), token);
    }

    public Task<List<ApiTokenInfo>> ListAsync(int userId) =>
        db.ApiTokens.Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Id)
            .Select(t => new ApiTokenInfo(t.Id, t.Name, t.CreatedAt, t.LastUsedAt))
            .ToListAsync();

    public async Task<ServiceResult> RevokeAsync(int userId, int tokenId)
    {
        var deleted = await db.ApiTokens.Where(t => t.Id == tokenId && t.UserId == userId).ExecuteDeleteAsync();
        return deleted == 1
            ? ServiceResult.Success()
            : ServiceResult.Fail(ErrorKind.NotFound, "token_not_found", "No token with that ID.");
    }

    // Returns the token's owner, or null if the token is unknown or revoked.
    public async Task<User?> AuthenticateAsync(string token)
    {
        if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        var hash = Hash(token);
        var entity = await db.ApiTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (entity?.User is null) return null;

        entity.LastUsedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return entity.User;
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static ApiTokenInfo ToInfo(ApiToken t) => new(t.Id, t.Name, t.CreatedAt, t.LastUsedAt);
}

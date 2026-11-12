using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RatCache.Web.Data;
using RatCache.Web.Models;

namespace RatCache.Web.Services;

// Input for creating a paste. Everything but Content is optional.
// Strings are validated here (NFR-3.5), so pages and the API share the same rules.
public record NewPaste(string? Content, string? Title = null, string? Language = null,
    string? Visibility = null, string? Expire = null);

// Input for editing a paste (FR-2.5). Null = leave unchanged. Title "" = remove the title.
// Expire restarts the clock from now ("1d" = one day from the edit).
public record PasteEdit(string? Content = null, string? Title = null, string? Language = null,
    string? Visibility = null, string? Expire = null);

// A decrypted paste.
public record PasteView(string Id, string? Title, string Content, string Language, Visibility Visibility,
    DateTime CreatedAt, DateTime? UpdatedAt, DateTime? ExpiresAt, int? OwnerId);

// A decrypted paste without its content, for lists.
public record PasteSummary(string Id, string? Title, string Language, Visibility Visibility,
    DateTime CreatedAt, DateTime? UpdatedAt, DateTime? ExpiresAt);

// FR-2.1 – FR-2.7. All paste reads and writes go through here, so encryption (NFR-1.2),
// visibility (FR-3.1/3.2) and expiry (FR-3.5) are checked in one place.
public class PasteService(RatCacheDbContext db, PasteEncryption crypto, TimeProvider time)
{
    public const int IdLength = 10;                       // NFR-5.2: at least 8
    public const int TitleMaxLength = 200;
    public const int AnonymousMaxBytes = 1024 * 1024;     // NFR-3.1: 1 MB
    public const int RegisteredMaxBytes = 5 * 1024 * 1024; // NFR-3.1: 5 MB

    // FR-3.3. Keys are what the web form and the CLI's --expire send.
    public static readonly IReadOnlyDictionary<string, Func<DateTime, DateTime?>> ExpireOptions =
        new Dictionary<string, Func<DateTime, DateTime?>>
        {
            ["10m"] = now => now.AddMinutes(10),
            ["1h"] = now => now.AddHours(1),
            ["1d"] = now => now.AddDays(1),
            ["1w"] = now => now.AddDays(7),
            ["1mo"] = now => now.AddMonths(1),
            ["never"] = _ => null,
        };

    private const string IdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string TitleField = "title";
    private const string ContentField = "content";

    // NFR-4.2: missing, expired and someone else's private paste all get this same error.
    public static readonly ServiceError NotFound =
        new(ErrorKind.NotFound, "paste_not_found", "That paste doesn't exist or has expired.");

    public async Task<ServiceResult<PasteView>> CreateAsync(NewPaste input, int? ownerId)
    {
        if (ValidateContent(input.Content, ownerId) is { } contentError) return contentError;
        var parsedTitle = ParseTitle(input.Title);
        if (parsedTitle.Error is { } titleError) return titleError;
        var language = ParseLanguage(input.Language);
        if (language.Error is { } langError) return langError;
        var visibility = ParseVisibility(input.Visibility, ownerId);
        if (visibility.Error is { } visError) return visError;
        var now = Now();
        var expires = ParseExpire(input.Expire, now);
        if (expires.Error is { } expError) return expError;

        var paste = new Paste
        {
            Id = await NewIdAsync(),
            OwnerId = ownerId,
            Language = language.Value,
            Visibility = visibility.Value,
            CreatedAt = now,
            ExpiresAt = expires.Value,
        };
        var title = parsedTitle.Value;
        paste.TitleCipher = title is null ? null : crypto.Encrypt(title, paste.Id, TitleField);
        paste.ContentCipher = crypto.Encrypt(input.Content!, paste.Id, ContentField);

        db.Pastes.Add(paste);
        await db.SaveChangesAsync();
        return new PasteView(paste.Id, title, input.Content!, paste.Language, paste.Visibility,
            paste.CreatedAt, paste.UpdatedAt, paste.ExpiresAt, paste.OwnerId);
    }

    // FR-2.3. viewerId is the signed-in user, or null.
    public async Task<ServiceResult<PasteView>> GetAsync(string id, int? viewerId)
    {
        var paste = await FindVisibleAsync(id, viewerId);
        return paste is null ? NotFound : ToView(paste);
    }

    // FR-2.7: newest first. Expired pastes are left out.
    public async Task<List<PasteSummary>> ListForUserAsync(int userId)
    {
        var now = Now();
        var pastes = await db.Pastes.AsNoTracking()
            .Where(p => p.OwnerId == userId && (p.ExpiresAt == null || p.ExpiresAt > now))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return pastes.Select(ToSummary).ToList();
    }

    // FR-2.5. Owner only; anonymous pastes can't be edited.
    public async Task<ServiceResult<PasteView>> UpdateAsync(string id, PasteEdit edit, int userId)
    {
        var paste = await FindVisibleAsync(id, userId);
        if (paste is null) return NotFound;
        if (paste.OwnerId != userId)
            return new ServiceError(ErrorKind.Forbidden, "not_owner", "You can only edit your own pastes.");

        var now = Now();
        if (edit.Content is not null)
        {
            if (ValidateContent(edit.Content, userId) is { } contentError) return contentError;
        }
        var title = ParseTitle(edit.Title);
        if (title.Error is { } titleError) return titleError;
        var language = edit.Language is null ? null : ParseLanguage(edit.Language);
        if (language?.Error is { } langError) return langError;
        var visibility = edit.Visibility is null ? null : ParseVisibility(edit.Visibility, userId);
        if (visibility?.Error is { } visError) return visError;
        var expires = edit.Expire is null ? null : ParseExpire(edit.Expire, now);
        if (expires?.Error is { } expError) return expError;

        if (edit.Content is not null) paste.ContentCipher = crypto.Encrypt(edit.Content, paste.Id, ContentField);
        if (edit.Title is not null)
            paste.TitleCipher = title.Value is null ? null : crypto.Encrypt(title.Value, paste.Id, TitleField);
        if (language is not null) paste.Language = language.Value!;
        if (visibility is not null) paste.Visibility = visibility.Value;
        if (expires is not null) paste.ExpiresAt = expires.Value;
        paste.UpdatedAt = now;

        await db.SaveChangesAsync();
        return ToView(paste);
    }

    // FR-2.6 (owner) and FR-7.1 (admin).
    public async Task<ServiceResult> DeleteAsync(string id, int userId, bool isAdmin)
    {
        var paste = isAdmin
            ? await db.Pastes.FindAsync(id)
            : await FindVisibleAsync(id, userId);
        if (paste is null) return NotFound;
        if (!isAdmin && paste.OwnerId != userId)
            return ServiceResult.Fail(ErrorKind.Forbidden, "not_owner", "You can only delete your own pastes.");

        db.Pastes.Remove(paste);
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // FR-3.5: permanently deletes every paste whose expiry has passed. Returns how many were removed.
   public Task<int> PurgeExpiredAsync()
   {
       var now = Now();
       return db.Pastes.Where(p => p.ExpiresAt != null && p.ExpiresAt <= now).ExecuteDeleteAsync();
   }

    // The paste if it exists, hasn't expired, and the viewer is allowed to see it.
    private async Task<Paste?> FindVisibleAsync(string id, int? viewerId)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 16) return null;
        var paste = await db.Pastes.FindAsync(id);
        if (paste is null) return null;
        if (paste.ExpiresAt is { } expires && expires <= Now()) return null;          // FR-3.5
        if (paste.Visibility == Visibility.Private && paste.OwnerId != viewerId) return null; // FR-3.1
        return paste;
    }

    private PasteView ToView(Paste p) => new(p.Id, DecryptTitle(p),
        crypto.Decrypt(p.ContentCipher, p.Id, ContentField),
        p.Language, p.Visibility, p.CreatedAt, p.UpdatedAt, p.ExpiresAt, p.OwnerId);

    private PasteSummary ToSummary(Paste p) =>
        new(p.Id, DecryptTitle(p), p.Language, p.Visibility, p.CreatedAt, p.UpdatedAt, p.ExpiresAt);

    private string? DecryptTitle(Paste p) =>
        p.TitleCipher is null ? null : crypto.Decrypt(p.TitleCipher, p.Id, TitleField);

    // NFR-5.2: cryptographic RNG, base62, never sequential.
    private async Task<string> NewIdAsync()
    {
        while (true)
        {
            var id = RandomNumberGenerator.GetString(IdAlphabet, IdLength);
            if (!await db.Pastes.AnyAsync(p => p.Id == id)) return id;
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    // ---- validation (NFR-3.1, 3.2, 3.5) ----

    private static ServiceError? ValidateContent(string? content, int? ownerId)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new ServiceError(ErrorKind.Invalid, "empty_paste", "A paste can't be empty.");

        var max = ownerId is null ? AnonymousMaxBytes : RegisteredMaxBytes;
        if (Encoding.UTF8.GetByteCount(content) > max)
            return new ServiceError(ErrorKind.TooLarge, "paste_too_large", ownerId is null
                ? "Pastes are limited to 1 MB. Sign in to paste up to 5 MB."
                : "Pastes are limited to 5 MB.");
        return null;
    }

    private static Parsed<string?> ParseTitle(string? title)
    {
        title = title?.Trim();
        if (string.IsNullOrEmpty(title)) return new(null);
        return title.Length > TitleMaxLength
            ? new(null, new ServiceError(ErrorKind.Invalid, "title_too_long",
                $"Titles can be at most {TitleMaxLength} characters."))
            : new(title);
    }

    private static Parsed<string> ParseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return new(Languages.Default);
        language = language.Trim().ToLowerInvariant();
        return Languages.IsValid(language)
            ? new(language)
            : new("", new ServiceError(ErrorKind.Invalid, "invalid_language",
                "Language must be one of: " + string.Join(", ", Languages.All.Select(l => l.Id)) + "."));
    }

    private static Parsed<Visibility> ParseVisibility(string? visibility, int? ownerId)
    {
        var value = visibility?.Trim().ToLowerInvariant() switch
        {
            null or "" or "public" => Visibility.Public,
            "unlisted" => Visibility.Unlisted,
            "private" => Visibility.Private,
            _ => (Visibility?)null,
        };
        if (value is null)
            return new(default, new ServiceError(ErrorKind.Invalid, "invalid_visibility",
                "Visibility must be public, unlisted, or private."));
        if (value == Visibility.Private && ownerId is null) // FR-3.2
            return new(default, new ServiceError(ErrorKind.Unauthorized, "sign_in_required",
                "Private pastes need an account. Sign in first."));
        return new(value.Value);
    }

    private static Parsed<DateTime?> ParseExpire(string? expire, DateTime now)
    {
        expire = string.IsNullOrWhiteSpace(expire) ? "never" : expire.Trim().ToLowerInvariant();
        return ExpireOptions.TryGetValue(expire, out var compute)
            ? new(compute(now))
            : new(null, new ServiceError(ErrorKind.Invalid, "invalid_expire",
                "Expire must be one of: " + string.Join(", ", ExpireOptions.Keys) + "."));
    }

    private record Parsed<T>(T Value, ServiceError? Error = null);
}

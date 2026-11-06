using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RatCache.Web.Data;
using RatCache.Web.Models;

namespace RatCache.Web.Services;

// FR-1.1 – FR-1.6: registration, credential checks, password change, account deletion.
// Signing in (setting the cookie) is done by the caller with AuthPrincipal.Create.
public partial class AccountService(RatCacheDbContext db, PasswordHasher hasher, TimeProvider time)
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 32;
    public const int PasswordMinLength = 12;   // NFR-2.2
    public const int PasswordMaxLength = 128;  // keeps hashing cost bounded

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex UsernameChars();

    // NFR-2.4: one error for both a wrong username and a wrong password.
    private static readonly ServiceError BadCredentials =
        new(ErrorKind.Unauthorized, "invalid_credentials", "Wrong username or password.");

    public async Task<ServiceResult<User>> RegisterAsync(string? username, string? password)
    {
        username = username?.Trim() ?? "";
        if (ValidateUsername(username) is { } usernameError) return usernameError;
        if (ValidatePassword(password) is { } passwordError) return passwordError;

        // FR-1.2. The column is NOCASE, so "Rat" and "rat" collide.
        if (await db.Users.AnyAsync(u => u.Username == username))
            return new ServiceError(ErrorKind.Conflict, "username_taken", "That username is already taken.");

        var user = new User
        {
            Username = username,
            PasswordHash = hasher.Hash(password!),
            SecurityStamp = NewSecurityStamp(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost a race with another registration of the same name.
            return new ServiceError(ErrorKind.Conflict, "username_taken", "That username is already taken.");
        }
        return user;
    }

    // FR-1.3. Returns the user if the username and password match.
    public async Task<ServiceResult<User>> CheckCredentialsAsync(string? username, string? password)
    {
        username = username?.Trim() ?? "";
        password ??= "";
        if (username.Length == 0 || password.Length == 0 || password.Length > PasswordMaxLength)
            return BadCredentials;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username);
        if (user is null)
        {
            hasher.VerifyDummy(password); // same timing as a real check
            return BadCredentials;
        }
        return hasher.Verify(password, user.PasswordHash) ? user : BadCredentials;
    }

    // FR-1.5. Rotates the security stamp, which signs out every other browser session.
    public async Task<ServiceResult<User>> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return new ServiceError(ErrorKind.Unauthorized, "sign_in_required", "Sign in first.");

        if (!hasher.Verify(currentPassword ?? "", user.PasswordHash))
            return new ServiceError(ErrorKind.Unauthorized, "invalid_credentials", "Current password is wrong.");
        if (ValidatePassword(newPassword) is { } passwordError) return passwordError;

        user.PasswordHash = hasher.Hash(newPassword!);
        user.SecurityStamp = NewSecurityStamp();
        await db.SaveChangesAsync();
        return user;
    }

    // FR-1.6. Deletes the user and, by cascade, their pastes and API tokens.
    public async Task<ServiceResult> DeleteAccountAsync(int userId, string? password)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return ServiceResult.Fail(ErrorKind.Unauthorized, "sign_in_required", "Sign in first.");

        if (!hasher.Verify(password ?? "", user.PasswordHash))
            return ServiceResult.Fail(ErrorKind.Unauthorized, "invalid_credentials", "Password is wrong.");

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // Used on every cookie-authenticated request: is this session still valid?
    public Task<bool> IsSessionValidAsync(int userId, string? securityStamp) =>
        db.Users.AnyAsync(u => u.Id == userId && u.SecurityStamp == securityStamp);

    private static ServiceError? ValidateUsername(string username)
    {
        if (username.Length < UsernameMinLength || username.Length > UsernameMaxLength || !UsernameChars().IsMatch(username))
            return new ServiceError(ErrorKind.Invalid, "invalid_username",
                $"Usernames are {UsernameMinLength}–{UsernameMaxLength} characters: letters, numbers, _ and -.");
        return null;
    }

    private static ServiceError? ValidatePassword(string? password)
    {
        if (password is null || password.Length < PasswordMinLength)
            return new ServiceError(ErrorKind.Invalid, "password_too_short",
                $"Passwords must be at least {PasswordMinLength} characters.");
        if (password.Length > PasswordMaxLength)
            return new ServiceError(ErrorKind.Invalid, "password_too_long",
                $"Passwords can be at most {PasswordMaxLength} characters.");
        return null;
    }

    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

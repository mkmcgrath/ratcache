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
    public const int MaxFailedSignIns = 5;     // NFR-2.3
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

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
    // NFR-2.3: 5 wrong passwords in a row lock the account for 15 minutes. While locked the password
    // isn't checked at all, so guessing right can't unlock it. A correct password resets the count.
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

        var now = time.GetUtcNow().UtcDateTime;
        if (user.LockoutUntil is { } until)
        {
            if (until > now)
            {
                var minutes = (int)Math.Ceiling((until - now).TotalMinutes);
                return new ServiceError(ErrorKind.TooManyRequests, "account_locked",
                    $"Too many failed sign-ins. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
            }
            // The lockout has ended: start counting from zero.
            user.LockoutUntil = null;
            user.FailedSignInCount = 0;
        }

        if (hasher.Verify(password, user.PasswordHash))
        {
            if (user.FailedSignInCount != 0)
            {
                user.FailedSignInCount = 0;
                await db.SaveChangesAsync();
            }
            return user;
        }

        user.FailedSignInCount++;
        if (user.FailedSignInCount >= MaxFailedSignIns)
        {
            user.LockoutUntil = now + LockoutDuration;
            user.FailedSignInCount = 0;
        }
        await db.SaveChangesAsync();
        return BadCredentials;
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

    // FR-7.1: makes exactly the named users admins and everyone else a normal user. Run at startup
    // with the RatCache:AdminUsernames setting. A user whose role changes gets a new security stamp,
    // which signs out their web sessions so the old role isn't kept in a cookie. Returns how many changed.
    public async Task<int> SyncAdminsAsync(IEnumerable<string> usernames)
    {
        var wanted = usernames.Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        var users = await db.Users.Where(u => u.IsAdmin || wanted.Contains(u.Username)).ToListAsync();
        var changed = 0;
        foreach (var user in users)
        {
            var shouldBeAdmin = wanted.Contains(user.Username, StringComparer.OrdinalIgnoreCase);
            if (user.IsAdmin == shouldBeAdmin) continue;
            user.IsAdmin = shouldBeAdmin;
            user.SecurityStamp = NewSecurityStamp();
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync();
        return changed;
    }

    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

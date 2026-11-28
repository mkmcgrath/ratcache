namespace RatCache.Web.Models;

// FR-1.1: a registered account. Username + password only, no email.
public class User
{
    public int Id { get; set; }

    // Unique, case-insensitive (NOCASE collation, see RatCacheDbContext). FR-1.2.
    public string Username { get; set; } = "";

    // bcrypt hash (NFR-2.1). Never the plaintext password.
    public string PasswordHash { get; set; } = "";

    // Random value stored in the sign-in cookie. Changing it (password change)
    // signs the user out everywhere.
    public string SecurityStamp { get; set; } = "";

    // FR-7.1: admins can delete any paste. Set from RatCache:AdminUsernames at startup (AccountService.SyncAdminsAsync).
    public bool IsAdmin { get; set; }

    // NFR-2.3: consecutive wrong passwords at sign-in. Reset by a correct password or once a lockout ends.
    public int FailedSignInCount { get; set; }

    // NFR-2.3: while this is in the future, sign-in is refused even with the right password.
    public DateTime? LockoutUntil { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Paste> Pastes { get; set; } = [];
    public List<ApiToken> ApiTokens { get; set; } = [];
}

namespace RatCache.Web.Services;

// NFR-2.1: bcrypt, cost 12. "Enhanced" mode pre-hashes with SHA-384 so
// passwords longer than bcrypt's 72-byte limit aren't silently truncated.
public sealed class PasswordHasher
{
    public const int WorkFactor = 12;

    // Used when the username doesn't exist, so a wrong username takes as long
    // as a wrong password (NFR-2.4).
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.EnhancedHashPassword(
        "ratcache-dummy-password", BCrypt.Net.HashType.SHA384, WorkFactor));

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, BCrypt.Net.HashType.SHA384, WorkFactor);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.EnhancedVerify(password, hash, BCrypt.Net.HashType.SHA384);

    public void VerifyDummy(string password) => Verify(password, DummyHash.Value);
}

namespace RatCache.Web.Models;

// FR-1.8: a CLI API token. Only a SHA-256 hash of the token is stored (NFR-2.6);
// the token itself is shown to the user once, when it's created.
public class ApiToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    // A label so the user can tell tokens apart, e.g. "laptop".
    public string Name { get; set; } = "";

    // Lowercase hex SHA-256 of the token.
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

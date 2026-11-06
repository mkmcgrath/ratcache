namespace RatCache.Web.Models;

// FR-2.x: one stored paste.
// Title and content are stored encrypted (NFR-1.2). Use PasteService to read or write them.
public class Paste
{
    // Random URL-safe ID (FR-2.2, NFR-5.2). Also the URL: /p/{Id}, /raw/{Id}.
    public string Id { get; set; } = "";

    // Null for anonymous pastes.
    public int? OwnerId { get; set; }
    public User? Owner { get; set; }

    // AES-256-GCM blobs. TitleCipher is null when the paste has no title.
    public byte[]? TitleCipher { get; set; }
    public byte[] ContentCipher { get; set; } = [];

    // A Languages.All id, e.g. "python".
    public string Language { get; set; } = Languages.Default;

    public Visibility Visibility { get; set; } = Visibility.Public;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Null means never expires (FR-3.3).
    public DateTime? ExpiresAt { get; set; }
}

// FR-3.1. Stored as lowercase text in the database ("public", "unlisted", "private").
public enum Visibility
{
    Public,
    Unlisted,
    Private,
}

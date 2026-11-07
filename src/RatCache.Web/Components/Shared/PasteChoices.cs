using RatCache.Web.Models;

namespace RatCache.Web.Components.Shared;

// Dropdown values for the new-paste and edit forms. The values are what PasteService accepts
// (the same strings the CLI sends); the labels are display only.
public static class PasteChoices
{
    public static readonly IReadOnlyList<(string Value, string Label)> VisibilityOptions =
    [
        ("public", "public"),
        ("unlisted", "unlisted"),
        ("private", "private"),
    ];

    // Keys match PasteService.ExpireOptions (FR-3.3).
    public static readonly IReadOnlyList<(string Value, string Label)> ExpireOptions =
    [
        ("never", "never"),
        ("10m", "10 minutes"),
        ("1h", "1 hour"),
        ("1d", "1 day"),
        ("1w", "1 week"),
        ("1mo", "1 month"),
    ];

    public static string LanguageLabel(string id) =>
        Languages.All.FirstOrDefault(l => l.Id == id).Label ?? id;

    public static string Lower(Visibility visibility) => visibility.ToString().ToLowerInvariant();

    // Browsers send textarea line breaks as \r\n. Store \n so /raw matches what was typed.
    public static string? NormalizeNewlines(string? text) => text?.Replace("\r\n", "\n");
}

// Fields shared by the new-paste form (Home) and the edit form (EditPaste).
public class PasteForm
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string Language { get; set; } = Languages.Default;
    public string Visibility { get; set; } = "public";
    public string Expire { get; set; } = "never";
}

namespace RatCache.Web.Models;

// FR-4.1 / FR-4.3: the languages a paste can be highlighted as.
// The Id is what's stored in the database, sent over the API, and passed to the CLI's --lang flag.
// The Label is only for display (e.g. the dropdown on the home page).
// Keep this list in sync with LANGUAGES in cli/ratcache/main.py.
public static class Languages
{
    public const string Default = "text";

    public static readonly IReadOnlyList<(string Id, string Label)> All =
    [
        ("text", "plain text"),
        ("bash", "Bash"),
        ("c", "C"),
        ("cpp", "C++"),
        ("css", "CSS"),
        ("go", "Go"),
        ("html", "HTML"),
        ("java", "Java"),
        ("javascript", "JavaScript"),
        ("json", "JSON"),
        ("markdown", "Markdown"),
        ("python", "Python"),
        ("rust", "Rust"),
        ("sql", "SQL"),
        ("yaml", "YAML"),
    ];

    public static bool IsValid(string? id) => All.Any(l => l.Id == id);
}

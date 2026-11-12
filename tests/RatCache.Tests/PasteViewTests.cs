using System.Net;
using System.Text.RegularExpressions;

namespace RatCache.Tests;

// The paste page's highlighting hooks (FR-4.2), line numbers (FR-4.4) and share bar (FR-2.9).
// The highlighting and copying themselves run in the browser (wwwroot/js/paste.js); these tests
// check the markup and assets they depend on.
public partial class PageTests
{
    [GeneratedRegex("<(?:script src|link rel=\"stylesheet\" href)=\"([^\"]+)\"")]
    private static partial Regex AssetRef();

    [Fact]
    public async Task PasteView_RendersLanguageLineNumbersAndShareBar()
    {
        var client = factory.NewClient();
        var id = await CreatePasteAsync(client, "a = 1\nb = <b>2</b>\nprint(a)\n");

        var html = await client.GetStringAsync($"/p/{id}");

        // Razor writes newlines in text as "&#xA;", which browsers show as a newline.
        Assert.Contains("<code data-lang=\"python\">a = 1&#xA;b = &lt;b&gt;2&lt;/b&gt;&#xA;print(a)&#xA;</code>", html);
        Assert.Contains("<pre class=\"line-numbers\" aria-hidden=\"true\">1&#xA;2&#xA;3</pre>", html);
        Assert.Contains($"data-share-bar data-id=\"{id}\" hidden", html);
        Assert.Contains("data-copy=\"link\"", html);
        Assert.Contains("data-copy=\"content\"", html);
    }

    [Theory]
    [InlineData("one", "1")]
    [InlineData("one\n", "1")]
    [InlineData("\n", "1")]
    [InlineData("a\nb", "1\n2")]
    [InlineData("a\r\nb\r\n", "1\n2")]
    [InlineData("a\rb", "1\n2")]
    [InlineData("a\n\n", "1\n2")]
    public void PasteView_LineNumbers_MatchVisibleLines(string content, string expected)
    {
        Assert.Equal(expected, RatCache.Web.Components.Pages.PasteView.LineNumbers(content));
    }

    [Fact]
    public async Task Layout_VendoredHighlightAndPasteScripts_AreServed()
    {
        var client = factory.NewClient();
        var html = await client.GetStringAsync("/");
        var refs = AssetRef().Matches(html).Select(m => m.Groups[1].Value).ToList();

        // Vendored, not from a CDN.
        foreach (var name in new[] { "lib/highlight/highlight.min.js", "lib/highlight/github.min.css", "js/paste.js" })
        {
            var href = refs.SingleOrDefault(r => r.StartsWith(name.Split('.')[0]) && r.Contains(Path.GetExtension(name)));
            Assert.True(href is not null, $"{name} isn't referenced by the layout");
            var response = await client.GetAsync("/" + href);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.DoesNotContain(refs, r => r.StartsWith("http"));
    }
}

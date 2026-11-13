using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RatCache.Web.Data;

namespace RatCache.Tests;

// FR-3.6: GET /api/pastes/recent and the /recent page list the 20 newest public pastes.
// Tests in this class share one database and run one at a time, so each test's own pastes
// are always the newest ones when it checks the list.
public class RecentTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private record Summary(string Id, string Url, string? Title, string Visibility);

    private static async Task<string> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/pastes", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Summary>())!.Id;
    }

    private async Task<List<Summary>> RecentAsync() =>
        (await factory.NewClient().GetFromJsonAsync<List<Summary>>("/api/pastes/recent"))!;

    [Fact]
    public async Task Api_ReturnsTwentyNewestPublic_NewestFirst()
    {
        var client = factory.NewClient();
        var ids = new List<string>();
        for (var i = 0; i < 21; i++) ids.Add(await CreateAsync(client, new { content = $"paste {i}" }));

        var recent = await RecentAsync();

        Assert.Equal(20, recent.Count);
        Assert.Equal(Enumerable.Reverse(ids).Take(20), recent.Select(p => p.Id));
        Assert.DoesNotContain(ids[0], recent.Select(p => p.Id));
        Assert.All(recent, p => Assert.Equal("public", p.Visibility));
    }

    [Fact]
    public async Task Api_LeavesOutUnlistedPrivateAndExpired()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var publicId = await CreateAsync(owner, new { content = "everyone", visibility = "public" });
        var unlistedId = await CreateAsync(owner, new { content = "link only", visibility = "unlisted" });
        var privateId = await CreateAsync(owner, new { content = "just me", visibility = "private" });
        var expiredId = await CreateAsync(owner, new { content = "gone soon", visibility = "public", expire = "10m" });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RatCacheDbContext>();
            var row = await db.Pastes.SingleAsync(p => p.Id == expiredId);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var ids = (await RecentAsync()).Select(p => p.Id).ToList();

        Assert.Contains(publicId, ids);
        Assert.DoesNotContain(unlistedId, ids);
        Assert.DoesNotContain(privateId, ids);
        Assert.DoesNotContain(expiredId, ids);
    }

    [Fact]
    public async Task Api_IsAnonymous_AndLeavesOutContent()
    {
        var id = await CreateAsync(factory.NewClient(), new { content = "secret-ish body", title = "listed title" });

        var response = await factory.NewClient().GetAsync("/api/pastes/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
        Assert.Equal("listed title", item.GetProperty("title").GetString());
        Assert.Equal($"https://localhost/p/{id}", item.GetProperty("url").GetString());
        Assert.False(item.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Page_ListsPublicPastes_EscapedAndLinked_ButNotUnlisted()
    {
        var client = factory.NewClient();
        var publicId = await CreateAsync(client, new { content = "x", title = "<b>shown</b>" });
        await CreateAsync(client, new { content = "x", title = "hidden-unlisted-title", visibility = "unlisted" });

        var html = await factory.NewClient().GetStringAsync("/recent");

        Assert.Contains($"href=\"p/{publicId}\"", html);
        Assert.Contains("&lt;b&gt;shown&lt;/b&gt;", html);
        Assert.DoesNotContain("hidden-unlisted-title", html);
        Assert.DoesNotContain("Not implemented yet", html);
    }

    [Fact]
    public async Task Page_WithNoPublicPastes_ShowsEmptyMessage()
    {
        using var fresh = new RatCacheFactory();

        var html = await fresh.NewClient().GetStringAsync("/recent");

        Assert.Contains("no public pastes yet", html);
    }
}

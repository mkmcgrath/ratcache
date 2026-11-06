using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RatCache.Web.Data;

namespace RatCache.Tests;

// FR-2.1 – FR-2.7, FR-3.1/3.2, NFR-1.2, NFR-3.x, NFR-4.2, NFR-5.2: /api/pastes and /raw.
public class PasteTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private record Created(string Id, string Url, string RawUrl, string? Title, string Language, string Visibility, DateTime? ExpiresAt);
    private record Full(string Id, string? Title, string Content, string Language, string Visibility, bool IsOwner, DateTime CreatedAt);

    private static async Task<Created> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/pastes", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>())!;
    }

    private static async Task<ErrorBody> ErrorOf(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<ErrorBody>())!;

    [Fact]
    public async Task CreateAnonymous_ReturnsLink_AndAnyoneCanRead()
    {
        var created = await CreateAsync(factory.NewClient(),
            new { content = "print('hi')", title = "hello", language = "python", visibility = "unlisted", expire = "1d" });

        Assert.Equal(10, created.Id.Length);
        Assert.Matches("^[A-Za-z0-9]+$", created.Id);
        Assert.Equal($"https://localhost/p/{created.Id}", created.Url);
        Assert.Equal("unlisted", created.Visibility);
        Assert.NotNull(created.ExpiresAt);

        var paste = await factory.NewClient().GetFromJsonAsync<Full>($"/api/pastes/{created.Id}");
        Assert.Equal("print('hi')", paste!.Content);
        Assert.Equal("hello", paste.Title);
        Assert.Equal("python", paste.Language);
        Assert.False(paste.IsOwner);
        Assert.Equal(DateTimeKind.Utc, paste.CreatedAt.Kind);
    }

    [Fact]
    public async Task Defaults_ArePublicPlainTextNeverExpires()
    {
        var created = await CreateAsync(factory.NewClient(), new { content = "x" });

        Assert.Equal("public", created.Visibility);
        Assert.Equal("text", created.Language);
        Assert.Null(created.ExpiresAt);
        Assert.Null(created.Title);
    }

    [Fact]
    public async Task Ids_AreRandom()
    {
        var client = factory.NewClient();
        var ids = new HashSet<string>();
        for (var i = 0; i < 20; i++) ids.Add((await CreateAsync(client, new { content = "x" })).Id);
        Assert.Equal(20, ids.Count);
    }

    [Fact]
    public async Task Raw_ReturnsPlainTextUnescaped_WithNoSniff()
    {
        const string html = "<script>alert(1)</script>\nline 2";
        var created = await CreateAsync(factory.NewClient(), new { content = html });

        var response = await factory.NewClient().GetAsync($"/raw/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(html, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ContentAndTitle_AreEncryptedAtRest()
    {
        const string secret = "my super secret paste body";
        const string title = "my secret title";
        var created = await CreateAsync(factory.NewClient(), new { content = secret, title });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RatCacheDbContext>();
        var row = await db.Pastes.AsNoTracking().SingleAsync(p => p.Id == created.Id);

        var blob = Encoding.UTF8.GetString(row.ContentCipher) + Encoding.UTF8.GetString(row.TitleCipher!);
        Assert.DoesNotContain(secret, blob);
        Assert.DoesNotContain(title, blob);
    }

    [Theory]
    [InlineData("", "empty_paste")]
    [InlineData("   \n\t ", "empty_paste")]
    public async Task Create_Empty_Returns400(string content, string code)
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/pastes", new { content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ErrorOf(response)).Error);
    }

    [Theory]
    [InlineData("language", "brainfuck", "invalid_language")]
    [InlineData("visibility", "secret", "invalid_visibility")]
    [InlineData("expire", "2y", "invalid_expire")]
    public async Task Create_InvalidOption_Returns400(string field, string value, string code)
    {
        var body = new Dictionary<string, string> { ["content"] = "x", [field] = value };
        var response = await factory.NewClient().PostAsJsonAsync("/api/pastes", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ErrorOf(response)).Error);
    }

    [Fact]
    public async Task Create_TitleTooLong_Returns400()
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/pastes", new { content = "x", title = new string('t', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SizeLimits_1MbAnonymous_5MbSignedIn()
    {
        var overOneMb = new string('a', 1024 * 1024 + 1);
        var anon = await factory.NewClient().PostAsJsonAsync("/api/pastes", new { content = overOneMb });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, anon.StatusCode);
        Assert.Equal("paste_too_large", (await ErrorOf(anon)).Error);

        var (client, _) = await factory.SignedInClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/pastes", new { content = overOneMb })).StatusCode);

        var overFiveMb = new string('a', 5 * 1024 * 1024 + 1);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await client.PostAsJsonAsync("/api/pastes", new { content = overFiveMb })).StatusCode);
    }

    [Fact]
    public async Task Private_RequiresSignIn()
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/pastes", new { content = "x", visibility = "private" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("sign_in_required", (await ErrorOf(response)).Error);
    }

    [Fact]
    public async Task Private_OnlyOwnerSees_OthersGetSameResponseAsMissing()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var (other, _) = await factory.SignedInClientAsync();
        var created = await CreateAsync(owner, new { content = "private stuff", visibility = "private" });

        var asOwner = await owner.GetFromJsonAsync<Full>($"/api/pastes/{created.Id}");
        Assert.True(asOwner!.IsOwner);

        foreach (var client in new[] { other, factory.NewClient() })
        {
            var privateResp = await client.GetAsync($"/api/pastes/{created.Id}");
            var missingResp = await client.GetAsync("/api/pastes/doesnotexist");
            Assert.Equal(HttpStatusCode.NotFound, privateResp.StatusCode);
            Assert.Equal(await missingResp.Content.ReadAsStringAsync(), await privateResp.Content.ReadAsStringAsync());

            var rawPrivate = await client.GetAsync($"/raw/{created.Id}");
            var rawMissing = await client.GetAsync("/raw/doesnotexist");
            Assert.Equal(HttpStatusCode.NotFound, rawPrivate.StatusCode);
            Assert.Equal(await rawMissing.Content.ReadAsStringAsync(), await rawPrivate.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/raw/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Expired_IsNotFound()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var created = await CreateAsync(owner, new { content = "short lived", expire = "10m" });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RatCacheDbContext>();
            var row = await db.Pastes.SingleAsync(p => p.Id == created.Id);
            row.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/pastes/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/raw/{created.Id}")).StatusCode);
        var mine = await owner.GetFromJsonAsync<List<Created>>("/api/pastes");
        Assert.DoesNotContain(mine!, p => p.Id == created.Id);
    }

    [Fact]
    public async Task List_ShowsOnlyMyPastes_NewestFirst()
    {
        var (me, _) = await factory.SignedInClientAsync();
        var (other, _) = await factory.SignedInClientAsync();
        var first = await CreateAsync(me, new { content = "1" });
        var second = await CreateAsync(me, new { content = "2", visibility = "private" });
        await CreateAsync(other, new { content = "not mine" });
        await CreateAsync(factory.NewClient(), new { content = "anonymous" });

        var mine = await me.GetFromJsonAsync<List<Created>>("/api/pastes");

        Assert.Equal([second.Id, first.Id], mine!.Select(p => p.Id));
    }

    [Fact]
    public async Task Edit_Owner_ChangesOnlyGivenFields()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var created = await CreateAsync(owner, new { content = "old", title = "keep me", language = "c" });

        var response = await owner.PatchAsJsonAsync($"/api/pastes/{created.Id}",
            new { content = "new", language = "rust", visibility = "private" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paste = await owner.GetFromJsonAsync<Full>($"/api/pastes/{created.Id}");
        Assert.Equal("new", paste!.Content);
        Assert.Equal("keep me", paste.Title);
        Assert.Equal("rust", paste.Language);
        Assert.Equal("private", paste.Visibility);

        await owner.PatchAsJsonAsync($"/api/pastes/{created.Id}", new { title = "" });
        Assert.Null((await owner.GetFromJsonAsync<Full>($"/api/pastes/{created.Id}"))!.Title);
    }

    [Fact]
    public async Task Edit_Invalid_Returns400AndChangesNothing()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var created = await CreateAsync(owner, new { content = "old" });

        var response = await owner.PatchAsJsonAsync($"/api/pastes/{created.Id}", new { content = "new", expire = "forever" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("old", (await owner.GetFromJsonAsync<Full>($"/api/pastes/{created.Id}"))!.Content);
    }

    [Fact]
    public async Task Edit_NotOwner_Forbidden_AnonymousPastesNotEditable_SignedOut401()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var (other, _) = await factory.SignedInClientAsync();
        var mine = await CreateAsync(owner, new { content = "x" });
        var anon = await CreateAsync(factory.NewClient(), new { content = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, (await other.PatchAsJsonAsync($"/api/pastes/{mine.Id}", new { content = "hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync($"/api/pastes/{anon.Id}", new { content = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().PatchAsJsonAsync($"/api/pastes/{mine.Id}", new { content = "y" })).StatusCode);
        Assert.Equal("x", (await owner.GetFromJsonAsync<Full>($"/api/pastes/{mine.Id}"))!.Content);
    }

    [Fact]
    public async Task Delete_OwnerOnly()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var (other, _) = await factory.SignedInClientAsync();
        var created = await CreateAsync(owner, new { content = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().DeleteAsync($"/api/pastes/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/pastes/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/pastes/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/pastes/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/pastes/{created.Id}")).StatusCode);
    }
}

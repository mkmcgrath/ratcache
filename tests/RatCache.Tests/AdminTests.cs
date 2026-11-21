using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RatCache.Web.Services;

namespace RatCache.Tests;

// FR-7.1: an administrator can delete any paste. Admins come from RatCache:AdminUsernames.
public class AdminTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private record Created(string Id);

    private async Task<int> SyncAsync(params string[] names)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountService>().SyncAdminsAsync(names);
    }

    // Signs in again so the new session carries the current role.
    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = factory.NewClient();
        (await client.PostAsJsonAsync("/api/sessions", new { username, password = TestData.Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string> CreatePasteAsync(HttpClient client, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/pastes", new { content = "secret", visibility });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    [Theory]
    [InlineData("public")]
    [InlineData("unlisted")]
    [InlineData("private")]
    public async Task Admin_CanDeleteAnyonesPaste(string visibility)
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var (_, adminName) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(owner, visibility);

        await SyncAsync(adminName);
        var admin = await SignInAsync(adminName);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/pastes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/pastes/{id}")).StatusCode);
        await SyncAsync(); // leave no admins behind for other tests
    }

    [Fact]
    public async Task NormalUser_CannotDeleteSomeoneElsesPaste()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var (other, _) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(owner, "public");

        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/pastes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/pastes/{id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_DeletingMissingPaste_Is404()
    {
        var (_, adminName) = await factory.SignedInClientAsync();
        await SyncAsync(adminName);
        var admin = await SignInAsync(adminName);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/pastes/doesnotexist")).StatusCode);
        await SyncAsync();
    }

    [Fact]
    public async Task Sync_PromotesAndDemotes_CaseInsensitive_AndEndsOldSessions()
    {
        var (client, name) = await factory.SignedInClientAsync();

        Assert.Equal(1, await SyncAsync(name.ToUpperInvariant()));
        Assert.Equal(0, await SyncAsync(name));          // already admin: nothing to do
        Assert.Equal(HttpStatusCode.Unauthorized,        // stale session from before the promotion
            (await client.GetAsync("/api/tokens")).StatusCode);

        Assert.Equal(1, await SyncAsync());              // removed from the list: demoted
        Assert.Equal(0, await SyncAsync("nobody-" + Guid.NewGuid().ToString("N")));
    }
}

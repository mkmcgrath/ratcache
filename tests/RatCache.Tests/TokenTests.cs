using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RatCache.Tests;

// FR-1.8, NFR-2.6: /api/tokens and Bearer authentication.
public class TokenTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private record NewToken(int Id, string Name, string Token);
    private record TokenInfo(int Id, string Name);

    private async Task<NewToken> CreateTokenAsync(HttpClient client, string name = "laptop")
    {
        var response = await client.PostAsJsonAsync("/api/tokens", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewToken>())!;
    }

    private HttpClient BearerClient(string token)
    {
        var client = factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CreateToken_WithoutSignIn_Returns401()
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/tokens", new { name = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_AuthenticatesAsItsUser()
    {
        var (client, _) = await factory.SignedInClientAsync();
        var token = await CreateTokenAsync(client);
        Assert.StartsWith("rc_", token.Token);

        var cli = BearerClient(token.Token);
        var created = await cli.PostAsJsonAsync("/api/pastes", new { content = "from the cli", visibility = "private" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var mine = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/pastes");
        Assert.Single(mine!);
    }

    [Fact]
    public async Task ListTokens_NeverIncludesSecret()
    {
        var (client, _) = await factory.SignedInClientAsync();
        var token = await CreateTokenAsync(client, "desktop");

        var json = await client.GetStringAsync("/api/tokens");

        Assert.Contains("desktop", json);
        Assert.DoesNotContain(token.Token, json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokedToken_IsRejected()
    {
        var (client, _) = await factory.SignedInClientAsync();
        var token = await CreateTokenAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tokens/{token.Id}")).StatusCode);

        var response = await BearerClient(token.Token).GetAsync("/api/pastes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_token", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }

    [Fact]
    public async Task RevokeSomeoneElsesToken_Returns404()
    {
        var (alice, _) = await factory.SignedInClientAsync();
        var (bob, _) = await factory.SignedInClientAsync();
        var token = await CreateTokenAsync(alice);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/tokens/{token.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BearerClient(token.Token).GetAsync("/api/pastes")).StatusCode);
    }

    [Fact]
    public async Task BadToken_OnAnonymousEndpoint_Returns401InsteadOfActingAnonymous()
    {
        var response = await BearerClient("rc_not-a-real-token").PostAsJsonAsync("/api/pastes", new { content = "hi" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_token", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }
}

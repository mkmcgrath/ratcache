using System.Net;
using System.Net.Http.Json;

namespace RatCache.Tests;

// Example API test: starts the whole app in memory and calls it over HTTP.
// Copy this pattern to test new endpoints (NFR-6.2).
public class HealthTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private readonly HttpClient _client = factory.NewClient();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ok", body?["status"]);
    }

    [Fact]
    public async Task HomePage_Loads()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

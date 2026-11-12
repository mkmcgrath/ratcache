using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace RatCache.Tests;

// NFR-4.3, NFR-4.4, NFR-1.4: request IDs, generic 500s, and what ends up in the logs.
public class ErrorLoggingTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private const string RequestIdHeader = "X-Request-Id";

    private static string RequestIdOf(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(RequestIdHeader, out var values), "missing X-Request-Id header");
        var id = values!.Single();
        Assert.False(string.IsNullOrWhiteSpace(id));
        return id;
    }

    [Fact]
    public async Task EveryResponse_HasARequestId()
    {
        var client = factory.NewClient();

        RequestIdOf(await client.GetAsync("/api/health"));          // API success
        RequestIdOf(await client.GetAsync("/api/pastes/nope"));     // API 404
        RequestIdOf(await client.GetAsync("/raw/nope"));            // raw 404
        RequestIdOf(await client.GetAsync("/p/nope"));              // web 404 page
    }

    [Fact]
    public async Task RequestIds_DifferBetweenRequests()
    {
        var client = factory.NewClient();
        var first = RequestIdOf(await client.GetAsync("/api/health"));
        var second = RequestIdOf(await client.GetAsync("/api/health"));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task UnhandledApiException_Returns500Json_WithRequestId_AndNoInternalDetails()
    {
        var response = await factory.NewClient().GetAsync("/api/test-boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var requestId = RequestIdOf(response);

        var raw = await response.Content.ReadAsStringAsync();
        var body = System.Text.Json.JsonSerializer.Deserialize<ErrorBody>(raw,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("server_error", body.Error);
        Assert.Contains(requestId, body.Message);

        // NFR-4.3: nothing internal leaks.
        Assert.DoesNotContain(BoomMessages.Secret, raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("RatCache.Tests", raw);
        Assert.DoesNotContain(" at ", raw);
    }

    [Fact]
    public async Task UnhandledRawException_Returns500PlainText_AndNoInternalDetails()
    {
        var response = await factory.NewClient().GetAsync("/raw/test-boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var requestId = RequestIdOf(response);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(requestId, text);
        Assert.DoesNotContain(BoomMessages.Secret, text);
        Assert.DoesNotContain("InvalidOperationException", text);
    }

    [Fact]
    public async Task UnhandledException_IsLoggedAsError_WithTheSameRequestId()
    {
        var response = await factory.NewClient().GetAsync("/api/test-boom");
        var requestId = RequestIdOf(response);

        Assert.Contains(factory.Logs.Entries, e =>
            e.Level == LogLevel.Error && e.Text.Contains(requestId) && e.Text.Contains("/api/test-boom"));
    }

    [Fact]
    public async Task Logs_NeverContainPasswordsTokensOrPasteContent()
    {
        // Every secret below contains this marker, so one search covers all of them.
        var marker = Guid.NewGuid().ToString("N");
        var username = "u" + marker[..12];
        var password = $"pw-{marker}-pw";
        var wrongPassword = $"wrong-{marker}-wrong";

        var client = factory.NewClient();
        (await client.PostAsJsonAsync("/api/users", new { username, password })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/sessions", new { username, password })).EnsureSuccessStatusCode();

        // A failed sign-in with a wrong password.
        await client.PostAsJsonAsync("/api/sessions", new { username, password = wrongPassword });

        // A private paste with secret title and content, read back through the API and /raw.
        var create = await client.PostAsJsonAsync("/api/pastes",
            new { content = $"content-{marker}", title = $"title-{marker}", visibility = "private" });
        create.EnsureSuccessStatusCode();
        var id = (await create.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["id"].ToString();
        await client.GetAsync($"/api/pastes/{id}");
        await client.GetAsync($"/raw/{id}");

        // Bad requests: an invalid paste, malformed JSON, and a bad API token.
        await client.PostAsJsonAsync("/api/pastes", new { content = "", title = $"title-{marker}" });
        await client.PostAsync("/api/pastes",
            new StringContent($"{{ \"content\": \"content-{marker}", System.Text.Encoding.UTF8, "application/json"));
        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/pastes");
        bad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"rc_{marker}");
        await factory.NewClient().SendAsync(bad);

        var leaks = factory.Logs.Entries.Where(e => e.Text.Contains(marker)).ToList();
        Assert.True(leaks.Count == 0,
            "Secrets found in logs:" + Environment.NewLine + string.Join(Environment.NewLine, leaks.Select(l => l.Text)));
    }
}
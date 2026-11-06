using System.Net;
using System.Net.Http.Json;

namespace RatCache.Tests;

// FR-1.1 – FR-1.6, NFR-2.x: /api/users and /api/sessions.
public class AuthTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private static string NewName() => "u" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Register_ReturnsCreated()
    {
        var name = NewName();
        var response = await factory.NewClient().PostAsJsonAsync("/api/users", new { username = name, password = TestData.Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal(name, body!["username"].ToString());
        Assert.False(body.ContainsKey("passwordHash"));
    }

    [Fact]
    public async Task Register_TakenUsername_IgnoringCase_ReturnsConflict()
    {
        var client = factory.NewClient();
        var name = NewName();
        await client.PostAsJsonAsync("/api/users", new { username = name, password = TestData.Password });

        var response = await client.PostAsJsonAsync("/api/users", new { username = name.ToUpperInvariant(), password = TestData.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("username_taken", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }

    [Theory]
    [InlineData("ab", TestData.Password, "invalid_username")]
    [InlineData("has space", TestData.Password, "invalid_username")]
    [InlineData("<script>", TestData.Password, "invalid_username")]
    [InlineData("validname", "short", "password_too_short")]
    [InlineData("validname", "elevenchars", "password_too_short")]
    public async Task Register_InvalidInput_ReturnsBadRequest(string username, string password, string code)
    {
        var response = await factory.NewClient().PostAsJsonAsync("/api/users", new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }

    [Fact]
    public async Task MalformedJson_ReturnsErrorShape()
    {
        var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");
        var response = await factory.NewClient().PostAsync("/api/users", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.Equal("invalid_request", body!.Error);
        Assert.DoesNotContain("Exception", body.Message);
    }

    [Fact]
    public async Task SignIn_SetsHttpOnlySecureSameSiteCookie()
    {
        var client = factory.NewClient();
        var name = NewName();
        await client.PostAsJsonAsync("/api/users", new { username = name, password = TestData.Password });

        var response = await client.PostAsJsonAsync("/api/sessions", new { username = name, password = TestData.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("ratcache_session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignIn_WrongPasswordAndUnknownUser_GiveSameError()
    {
        var client = factory.NewClient();
        var name = NewName();
        await client.PostAsJsonAsync("/api/users", new { username = name, password = TestData.Password });

        var wrongPassword = await client.PostAsJsonAsync("/api/sessions", new { username = name, password = "wrong password!!" });
        var unknownUser = await client.PostAsJsonAsync("/api/sessions", new { username = NewName(), password = TestData.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignedInRequest_Works_AndSignOutEndsSession()
    {
        var (client, _) = await factory.SignedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pastes")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/sessions")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/pastes")).StatusCode);
    }

    [Fact]
    public async Task ProtectedApi_WithoutSignIn_Returns401JsonNotRedirect()
    {
        var response = await factory.NewClient().GetAsync("/api/pastes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("sign_in_required", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordWorks_OtherSessionsSignedOut()
    {
        var (client, name) = await factory.SignedInClientAsync();
        var otherBrowser = factory.NewClient();
        await otherBrowser.PostAsJsonAsync("/api/sessions", new { username = name, password = TestData.Password });
        const string newPassword = "a whole new password";

        var wrongCurrent = await client.PutAsJsonAsync("/api/users/me/password",
            new { currentPassword = "not my password", newPassword });
        var response = await client.PutAsJsonAsync("/api/users/me/password",
            new { currentPassword = TestData.Password, newPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pastes")).StatusCode);            // this session kept
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync("/api/pastes")).StatusCode); // other one dropped
        var oldLogin = await factory.NewClient().PostAsJsonAsync("/api/sessions", new { username = name, password = TestData.Password });
        var newLogin = await factory.NewClient().PostAsJsonAsync("/api/sessions", new { username = name, password = newPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_RemovesUserAndPastes()
    {
        var (client, name) = await factory.SignedInClientAsync();
        var created = await client.PostAsJsonAsync("/api/pastes", new { content = "mine" });
        var id = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["id"].ToString();

        var wrong = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/me")
            { Content = JsonContent.Create(new { password = "not my password" }) });
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/me")
            { Content = JsonContent.Create(new { password = TestData.Password }) });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.NewClient().GetAsync($"/api/pastes/{id}")).StatusCode);
        var signIn = await factory.NewClient().PostAsJsonAsync("/api/sessions", new { username = name, password = TestData.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }
}

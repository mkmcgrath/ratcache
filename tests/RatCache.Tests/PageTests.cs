using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace RatCache.Tests;

// The Blazor pages (FR-1.x, FR-2.x through the web UI). Each test GETs a page for its
// antiforgery token, then POSTs the form the way a browser would.
public partial class PageTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string formName,
        Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(url);
        var token = AntiforgeryField().Match(html);
        Assert.True(token.Success, $"no antiforgery token on {url}");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        fields["_handler"] = formName;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static string Location(HttpResponseMessage response) =>
        response.Headers.Location is { } l ? (l.IsAbsoluteUri ? l.PathAndQuery : l.OriginalString) : "";

    private static async Task<string> CreatePasteAsync(HttpClient client, string content, string visibility = "public")
    {
        var response = await PostFormAsync(client, "/", "new-paste", new()
        {
            ["Input.Content"] = content, ["Input.Language"] = "python",
            ["Input.Visibility"] = visibility, ["Input.Expire"] = "never",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Location(response);
        Assert.StartsWith("/p/", location);
        return location["/p/".Length..];
    }

    [Fact]
    public async Task HomeForm_Anonymous_CreatesPasteAndShowsItEscaped()
    {
        var client = factory.NewClient();
        var id = await CreatePasteAsync(client, "<script>alert(1)</script>\r\nline2");

        var html = await client.GetStringAsync($"/p/{id}");
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.Equal("<script>alert(1)</script>\nline2", await client.GetStringAsync($"/raw/{id}"));
    }

    [Fact]
    public async Task HomeForm_EmptyContent_ShowsError()
    {
        var response = await PostFormAsync(factory.NewClient(), "/", "new-paste", new() { ["Input.Content"] = "  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A paste can&#x27;t be empty.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HomeForm_RegisteredUser_AcceptsPasteOverFourMegabytes()
    {
        var (client, _) = await factory.SignedInClientAsync();
        await CreatePasteAsync(client, new string('a', 4_500_000));
    }

    [Fact]
    public async Task PasteView_Missing_Returns404()
    {
        var response = await factory.NewClient().GetAsync("/p/doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasteView_SomeoneElsesPrivatePaste_Returns404()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(owner, "secret", "private");

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/p/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.NewClient().GetAsync($"/p/{id}")).StatusCode);
    }

    [Fact]
    public async Task RegisterForm_SignsInAndNavShowsUsername()
    {
        var client = factory.NewClient();
        var name = "u" + Guid.NewGuid().ToString("N")[..12];

        var response = await PostFormAsync(client, "/account", "register", new()
        {
            ["Input.Username"] = name, ["Input.Password"] = TestData.Password, ["Input.Confirm"] = TestData.Password,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var account = await client.GetStringAsync("/account");
        Assert.Contains($"signed in as <strong>{name}</strong>", account);
    }

    [Fact]
    public async Task SignInForm_WrongPassword_ShowsErrorAndDoesNotEchoPassword()
    {
        var response = await PostFormAsync(factory.NewClient(), "/account", "sign-in", new()
        {
            ["Input.Username"] = "nobody-here", ["Input.Password"] = "hunter2hunter2",
        });

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Wrong username or password.", html);
        Assert.DoesNotContain("hunter2hunter2", html);
    }

    [Theory]
    [InlineData("/my", "/my")]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    public async Task SignInForm_FollowsOnlyLocalReturnUrls(string returnUrl, string expected)
    {
        var client = factory.NewClient();
        var name = "u" + Guid.NewGuid().ToString("N")[..12];
        await client.PostAsJsonAsync("/api/users", new { username = name, password = TestData.Password });

        var response = await PostFormAsync(client, "/account?ReturnUrl=" + Uri.EscapeDataString(returnUrl), "sign-in", new()
        {
            ["Input.Username"] = name, ["Input.Password"] = TestData.Password,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, Location(response));
    }

    [Fact]
    public async Task MyPastes_SignedOut_RedirectsToAccount()
    {
        var response = await factory.NewClient().GetAsync("/my");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account?ReturnUrl=", Location(response));
    }

    [Fact]
    public async Task MyPastes_ListsOwnPastes()
    {
        var (client, _) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(client, "mine");

        Assert.Contains($"href=\"p/{id}\"", await client.GetStringAsync("/my"));
    }

    [Fact]
    public async Task EditForm_OwnerCanEdit_OthersCannot()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(owner, "before");

        var response = await PostFormAsync(owner, $"/p/{id}/edit", "edit-paste", new()
        {
            ["Input.Title"] = "new title", ["Input.Content"] = "after", ["Input.Language"] = "rust",
            ["Input.Visibility"] = "unlisted", ["Input.Expire"] = "keep",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("after", await owner.GetStringAsync($"/raw/{id}"));
        Assert.Contains("<h1>new title</h1>", await owner.GetStringAsync($"/p/{id}"));

        var (other, _) = await factory.SignedInClientAsync();
        var html = await other.GetStringAsync($"/p/{id}/edit");
        Assert.Contains("you can only edit your own pastes", html);
        Assert.DoesNotContain("name=\"Input.Content\"", html);
    }

    [Fact]
    public async Task DeleteButton_OwnerDeletesPaste()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(owner, "delete me");

        var response = await PostFormAsync(owner, $"/p/{id}", "delete-paste", new());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/raw/{id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteButton_NotShownToOthers()
    {
        var (owner, _) = await factory.SignedInClientAsync();
        var id = await CreatePasteAsync(owner, "keep me");

        Assert.DoesNotContain("delete paste", await factory.NewClient().GetStringAsync($"/p/{id}"));
    }

    [Fact]
    public async Task ApiTokens_CreateShowsTokenThatWorksThenRevoke()
    {
        var (client, _) = await factory.SignedInClientAsync();

        var response = await PostFormAsync(client, "/account", "create-token", new() { ["Input.Name"] = "laptop" });
        var token = Regex.Match(await response.Content.ReadAsStringAsync(), "<pre>(rc_[^<]+)</pre>").Groups[1].Value;
        Assert.NotEmpty(token);

        var cli = factory.NewClient();
        cli.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await cli.GetAsync("/api/pastes")).StatusCode);

        var tokenId = Regex.Match(await client.GetStringAsync("/account"), "name=\"RevokeId\" value=\"(\\d+)\"").Groups[1].Value;
        await PostFormAsync(client, "/account", "revoke-token", new() { ["RevokeId"] = tokenId });
        Assert.Equal(HttpStatusCode.Unauthorized, (await cli.GetAsync("/api/pastes")).StatusCode);
    }

    [Fact]
    public async Task ChangePasswordForm_KeepsThisSessionSignedIn()
    {
        var (client, name) = await factory.SignedInClientAsync();
        const string newPassword = "a brand new password";

        var response = await PostFormAsync(client, "/account", "change-password", new()
        {
            ["Input.Current"] = TestData.Password, ["Input.New"] = newPassword, ["Input.Confirm"] = newPassword,
        });

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("password changed", html);
        Assert.DoesNotContain(newPassword, html);
        Assert.Contains($"signed in as <strong>{name}</strong>", await client.GetStringAsync("/account"));
    }

    [Fact]
    public async Task DeleteAccountForm_DeletesAndSignsOut()
    {
        var (client, name) = await factory.SignedInClientAsync();

        var response = await PostFormAsync(client, "/account", "delete-account", new() { ["Input.Password"] = TestData.Password });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("signed in as", await client.GetStringAsync("/account"));
        var signIn = await factory.NewClient().PostAsJsonAsync("/api/sessions", new { username = name, password = TestData.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    [Fact]
    public async Task SignOutForm_SignsOut()
    {
        var (client, _) = await factory.SignedInClientAsync();

        var response = await PostFormAsync(client, "/account", "sign-out", new());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/my")).StatusCode);
    }
}

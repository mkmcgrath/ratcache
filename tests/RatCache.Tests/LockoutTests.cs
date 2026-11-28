using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RatCache.Tests;

// NFR-2.3: 5 wrong passwords in a row lock the account for 15 minutes.
public class LockoutTests : IClassFixture<RatCacheFactory>
{
    // A clock the test can move forward.
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private readonly TestClock _clock = new();
    private readonly RatCacheFactory _factory;

    public LockoutTests(RatCacheFactory factory) =>
        _factory = factory;

    private HttpClient NewClient() =>
        _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock))))
        .CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var username = "l" + Guid.NewGuid().ToString("N")[..12];
        (await client.PostAsJsonAsync("/api/users", new { username, password = TestData.Password })).EnsureSuccessStatusCode();
        return username;
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/sessions", new { username, password });

    private static async Task FailAsync(HttpClient client, string username, int times)
    {
        for (var i = 0; i < times; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, username, "wrong password here")).StatusCode);
    }

    [Fact]
    public async Task FifthWrongPassword_LocksTheAccount_EvenForTheRightPassword()
    {
        var client = NewClient();
        var username = await RegisterAsync(client);

        await FailAsync(client, username, 5);

        var locked = await SignInAsync(client, username, TestData.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        var body = await locked.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.Equal("account_locked", body!.Error);
    }

    [Fact]
    public async Task FourWrongPasswords_DoNotLock()
    {
        var client = NewClient();
        var username = await RegisterAsync(client);

        await FailAsync(client, username, 4);

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client, username, TestData.Password)).StatusCode);
    }

    [Fact]
    public async Task CorrectPassword_ResetsTheCount()
    {
        var client = NewClient();
        var username = await RegisterAsync(client);

        await FailAsync(client, username, 4);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client, username, TestData.Password)).StatusCode);
        await FailAsync(client, username, 4); // would be 8 in a row if the count hadn't reset

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client, username, TestData.Password)).StatusCode);
    }

    [Fact]
    public async Task Lockout_EndsAfter15Minutes_AndCountStartsOver()
    {
        var client = NewClient();
        var username = await RegisterAsync(client);
        await FailAsync(client, username, 5);

        _clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(client, username, TestData.Password)).StatusCode);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await FailAsync(client, username, 4); // a fresh count, so 4 more is still not a lock
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client, username, TestData.Password)).StatusCode);
    }

    [Fact]
    public async Task Lockout_OnlyAffectsThatAccount()
    {
        var client = NewClient();
        var victim = await RegisterAsync(client);
        var other = await RegisterAsync(client);

        await FailAsync(client, victim, 5);

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client, other, TestData.Password)).StatusCode);
    }
}

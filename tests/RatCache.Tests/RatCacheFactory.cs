using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace RatCache.Tests;

// Starts the app in memory with its own throwaway in-memory SQLite database and a random
// encryption key, so tests never touch the real ratcache.db or need user-secrets.
// Use it as IClassFixture<RatCacheFactory>.
public class RatCacheFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString =
        $"Data Source=ratcache-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    // An in-memory SQLite database only lives while a connection to it is open.
    private readonly SqliteConnection _keepAlive;

    public RatCacheFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("RatCache:EncryptionKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("RatCache:PurgeIntervalMinutes", "0");
    }

    // A client with its own cookie jar. https, because the session cookie is Secure-only.
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    // Registers a new user with a unique name and returns a client signed in as them.
    public async Task<(HttpClient Client, string Username)> SignedInClientAsync(string password = TestData.Password)
    {
        var client = NewClient();
        var username = "u" + Guid.NewGuid().ToString("N")[..12];
        (await client.PostAsJsonAsync("/api/users", new { username, password })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/sessions", new { username, password })).EnsureSuccessStatusCode();
        return (client, username);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}

public static class TestData
{
    public const string Password = "correct horse battery";
}

// The NFR-4.1 error body.
public record ErrorBody(string Error, string Message);

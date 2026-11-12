using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RatCache.Web.Data;
using RatCache.Web.Services;

namespace RatCache.Tests;

// FR-3.5: expired pastes are permanently deleted, not just hidden.
public class PurgeTests(RatCacheFactory factory) : IClassFixture<RatCacheFactory>
{
    private record Created(string Id);

    private async Task<string> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/pastes", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    [Fact]
    public async Task Purge_DeletesExpiredPastes_AndKeepsTheRest()
    {
        var client = factory.NewClient();
        var expiredId = await CreateAsync(client, new { content = "old", expire = "10m" });
        var futureId = await CreateAsync(client, new { content = "still good", expire = "1d" });
        var neverId = await CreateAsync(client, new { content = "forever", expire = "never" });

        // Push one paste's expiry into the past, as the existing Expired_IsNotFound test does.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RatCacheDbContext>();
            var row = await db.Pastes.SingleAsync(p => p.Id == expiredId);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        int removed;
        using (var scope = factory.Services.CreateScope())
        {
            removed = await scope.ServiceProvider.GetRequiredService<PasteService>().PurgeExpiredAsync();
        }

        Assert.True(removed >= 1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RatCacheDbContext>();
            Assert.False(await db.Pastes.AnyAsync(p => p.Id == expiredId));
            Assert.True(await db.Pastes.AnyAsync(p => p.Id == futureId));
            Assert.True(await db.Pastes.AnyAsync(p => p.Id == neverId));
        }
    }

    [Fact]
    public async Task Purge_WithNothingExpired_ReturnsZeroAndChangesNothing()
    {
        var client = factory.NewClient();
        var id = await CreateAsync(client, new { content = "fresh", expire = "1h" });

        using var scope = factory.Services.CreateScope();
        var removed = await scope.ServiceProvider.GetRequiredService<PasteService>().PurgeExpiredAsync();
        var db = scope.ServiceProvider.GetRequiredService<RatCacheDbContext>();

        Assert.Equal(0, removed);
        Assert.True(await db.Pastes.AnyAsync(p => p.Id == id));
    }
}
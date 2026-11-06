using Microsoft.EntityFrameworkCore;
using RatCache.Web.Models;

namespace RatCache.Web.Data;

// The app's database (SQLite). Only Services/ use this directly.
// After changing a model, add a migration:
//     dotnet ef migrations add <Name> --project src/RatCache.Web
// Migrations are applied automatically at startup (Program.cs).
public class RatCacheDbContext(DbContextOptions<RatCacheDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Paste> Pastes => Set<Paste>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    // SQLite has no timezone type. All times are stored as UTC and come back marked as UTC.
    protected override void ConfigureConventions(ModelConfigurationBuilder b) =>
        b.Properties<DateTime>().HaveConversion<UtcConverter>();

    private class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(32).UseCollation("NOCASE");
            e.HasIndex(u => u.Username).IsUnique();
        });

        b.Entity<Paste>(e =>
        {
            e.Property(p => p.Id).HasMaxLength(16);
            e.Property(p => p.Language).HasMaxLength(32);
            e.Property(p => p.Visibility)
                .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<Visibility>(s, true))
                .HasMaxLength(16);
            // FR-1.6: deleting an account deletes its pastes.
            e.HasOne(p => p.Owner).WithMany(u => u.Pastes)
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.OwnerId, p.CreatedAt });
            e.HasIndex(p => new { p.Visibility, p.CreatedAt });
            e.HasIndex(p => p.ExpiresAt);
        });

        b.Entity<ApiToken>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(64);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User).WithMany(u => u.ApiTokens)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

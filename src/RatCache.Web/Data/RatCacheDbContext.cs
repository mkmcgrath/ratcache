using Microsoft.EntityFrameworkCore;

namespace RatCache.Web.Data;

// The app's database (SQLite). No tables yet.
// Whoever adds the first model: add a DbSet here, e.g.
//     public DbSet<Paste> Pastes => Set<Paste>();
// then create the first migration:
//     dotnet tool install --global dotnet-ef
//     dotnet ef migrations add InitialCreate --project src/RatCache.Web
public class RatCacheDbContext(DbContextOptions<RatCacheDbContext> options) : DbContext(options)
{
}

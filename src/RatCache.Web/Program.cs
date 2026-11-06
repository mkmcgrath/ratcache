using Microsoft.EntityFrameworkCore;
using RatCache.Web.Components;
using RatCache.Web.Controllers;
using RatCache.Web.Data;
using RatCache.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----
// Blazor pages. Pages render on the server as plain HTML by default.
// Add @rendermode InteractiveServer to a page only if it needs live interactivity.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// API controllers (Controllers/). The CLI calls these over HTTP; Blazor pages call Services/ directly.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiErrors.FromModelState); // NFR-4.1 error shape

// OpenAPI description of the API, served at /openapi/v1.json in Development.
builder.Services.AddOpenApi();

// SQLite database. Connection string lives in appsettings.json.
builder.Services.AddDbContext<RatCacheDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Register your own services (Services/) here, e.g.:
// builder.Services.AddScoped<IPasteService, PasteService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordHasher>();       // NFR-2.1
builder.Services.AddSingleton<PasteEncryption>();      // NFR-1.2 (needs RatCache:EncryptionKey)
builder.Services.AddScoped<AccountService>();          // FR-1.1 – 1.6
builder.Services.AddScoped<ApiTokenService>();         // FR-1.8
builder.Services.AddScoped<PasteService>();            // FR-2.x

// Sign-in: session cookie for the web, Bearer API token for the CLI (Services/AuthenticationSetup.cs).
builder.Services.AddRatCacheAuth();

var app = builder.Build();

// Fail at startup, not on the first paste, if the encryption key is missing or invalid.
_ = app.Services.GetRequiredService<PasteEncryption>();

// Apply any pending EF Core migrations (creates ratcache.db on first run).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<RatCacheDbContext>().Database.Migrate();
}

// ---- HTTP pipeline ----
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Show the NotFound page for unknown web URLs. API routes keep their own status codes.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/raw"),
    web => web.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();
app.UseRouting(); // after the status-code pages above, so /not-found can be re-routed
app.UseRatCacheAuthentication(); // UseAuthentication + reject bad API tokens
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Lets the test project start the app in memory (WebApplicationFactory<Program>).
public partial class Program { }

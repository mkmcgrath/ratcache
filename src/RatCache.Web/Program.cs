using Microsoft.EntityFrameworkCore;
using RatCache.Web.Components;
using RatCache.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----
// Blazor pages. Pages render on the server as plain HTML by default.
// Add @rendermode InteractiveServer to a page only if it needs live interactivity.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// API controllers (Controllers/). The CLI calls these over HTTP; Blazor pages call Services/ directly.
builder.Services.AddControllers();

// OpenAPI description of the API, served at /openapi/v1.json in Development.
builder.Services.AddOpenApi();

// SQLite database. Connection string lives in appsettings.json.
builder.Services.AddDbContext<RatCacheDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Register your own services (Services/) here, e.g.:
// builder.Services.AddScoped<IPasteService, PasteService>();

var app = builder.Build();

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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Lets the test project start the app in memory (WebApplicationFactory<Program>).
public partial class Program { }

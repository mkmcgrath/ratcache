using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RatCache.Web.Controllers;

namespace RatCache.Web.Services;

// FR-1.3 / FR-1.4 / FR-1.8 / NFR-2.5.
// Two ways to be signed in, both giving the same ClaimsPrincipal (see AuthPrincipal):
//   - web: an HttpOnly, Secure, SameSite=Lax session cookie ("ratcache_session")
//   - CLI: "Authorization: Bearer rc_..." API token
// The default "ratcache" scheme picks between them per request.
public static class AuthenticationSetup
{
    public const string DefaultScheme = "ratcache";
    public const string CookieName = "ratcache_session";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);

    public static IServiceCollection AddRatCacheAuth(this IServiceCollection services)
    {
        services.AddAuthentication(DefaultScheme)
            .AddPolicyScheme(DefaultScheme, DefaultScheme, o =>
                o.ForwardDefaultSelector = ctx => ApiTokenAuthenticationHandler.HasBearerHeader(ctx.Request)
                    ? ApiTokenAuthenticationHandler.SchemeName
                    : CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = SessionLifetime; // FR-1.4: valid until sign-out or expiry
                o.SlidingExpiration = true;
                o.LoginPath = "/account";
                o.AccessDeniedPath = "/account";
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
                // The API answers 401/403 with JSON instead of redirecting to the sign-in page.
                o.Events.OnRedirectToLogin = ctx => IsApi(ctx.Request)
                    ? ApiErrors.WriteAsync(ctx.Response, StatusCodes.Status401Unauthorized, "sign_in_required", "Sign in first.")
                    : Redirect(ctx);
                o.Events.OnRedirectToAccessDenied = ctx => IsApi(ctx.Request)
                    ? ApiErrors.WriteAsync(ctx.Response, StatusCodes.Status403Forbidden, "forbidden", "You can't do that.")
                    : Redirect(ctx);
            })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenAuthenticationHandler.SchemeName, null);

        services.AddAuthorization();
        return services;
    }

    // Use in place of UseAuthentication(). A request that sends a bad API token gets a 401
    // instead of quietly being treated as anonymous (which would, for example, create an
    // anonymous paste when the user meant to create one on their account).
    public static IApplicationBuilder UseRatCacheAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        return app.Use(async (ctx, next) =>
        {
            if (ApiTokenAuthenticationHandler.HasBearerHeader(ctx.Request) && ctx.User.GetUserId() is null)
            {
                await ctx.ChallengeAsync(ApiTokenAuthenticationHandler.SchemeName);
                return;
            }
            await next(ctx);
        });
    }

    // Rejects a cookie whose user was deleted or whose password changed since sign-in.
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var accounts = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
        var userId = ctx.Principal?.GetUserId();
        var stamp = ctx.Principal?.FindFirst(AuthPrincipal.SecurityStampClaim)?.Value;
        if (userId is null || !await accounts.IsSessionValidAsync(userId.Value, stamp))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

    private static Task Redirect(RedirectContext<CookieAuthenticationOptions> ctx)
    {
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
}

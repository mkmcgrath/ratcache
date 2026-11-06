using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using RatCache.Web.Controllers;

namespace RatCache.Web.Services;

// "Authorization: Bearer <token>" for the CLI (SRS §5.2, FR-1.8).
public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Bearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryGetBearerToken(Request, out var token))
            return AuthenticateResult.NoResult();

        var user = await tokens.AuthenticateAsync(token);
        if (user is null)
            return AuthenticateResult.Fail("Invalid API token."); // never log the token itself (NFR-1.4)

        var principal = AuthPrincipal.Create(user, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return ApiErrors.WriteAsync(Response, StatusCodes.Status401Unauthorized,
            "invalid_token", "That API token is invalid or has been revoked. Run `ratcache login` again.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ApiErrors.WriteAsync(Response, StatusCodes.Status403Forbidden, "forbidden", "You can't do that.");

    public static bool HasBearerHeader(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBearerToken(HttpRequest request, out string token)
    {
        token = "";
        if (!HasBearerHeader(request)) return false;
        token = request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        return token.Length > 0;
    }
}

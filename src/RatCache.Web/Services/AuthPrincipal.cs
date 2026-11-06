using System.Security.Claims;
using RatCache.Web.Models;

namespace RatCache.Web.Services;

// Builds and reads the signed-in user's identity. The cookie (web) and API token (CLI)
// both produce the same claims, so pages and controllers don't care which was used.
public static class AuthPrincipal
{
    public const string SecurityStampClaim = "ratcache:stamp";
    public const string AdminRole = "admin";

    public static ClaimsPrincipal Create(User user, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(SecurityStampClaim, user.SecurityStamp),
        };
        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    // Null when the request isn't signed in.
    public static int? GetUserId(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
        && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(AdminRole);
}

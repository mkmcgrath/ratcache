using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RatCache.Web.Models;
using RatCache.Web.Services;

namespace RatCache.Web.Components.Account;

// Cookie sign-in for the web pages (FR-1.3), the same way SessionsController does it for the API.
// Pages that sign in or out must be static SSR: the cookie is set on the HTTP response.
public static class WebSignIn
{
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static Task SignInAsync(HttpContext context, User user) =>
        context.SignInAsync(Scheme, AuthPrincipal.Create(user, Scheme),
            new AuthenticationProperties { IsPersistent = true });

    public static Task SignOutAsync(HttpContext context) => context.SignOutAsync(Scheme);

    // Only follow ?ReturnUrl= when it stays on this site. "//evil.com" and "/\evil.com" are
    // other sites to a browser, and browsers drop tabs/newlines, so reject control characters too.
    public static string LocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.Any(char.IsControl)) return "/";
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\')) return "/";
        return url;
    }
}

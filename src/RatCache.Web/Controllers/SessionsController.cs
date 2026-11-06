using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RatCache.Web.Services;

namespace RatCache.Web.Controllers;

public record SignInRequest(string? Username, string? Password);
public record SessionResponse(string Username);

// FR-1.3 / FR-1.4: sign in and out with a session cookie.
[ApiController]
[Route("api/sessions")]
public class SessionsController(AccountService accounts) : ControllerBase
{
    // POST /api/sessions  {username, password} -> 200 + Set-Cookie: ratcache_session=...
    [HttpPost]
    public async Task<IActionResult> SignIn(SignInRequest req)
    {
        var result = await accounts.CheckCredentialsAsync(req.Username, req.Password);
        if (!result.Ok) return ApiErrors.From(result.Error!);

        var user = result.Value!;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AuthPrincipal.Create(user, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = true });
        return Ok(new SessionResponse(user.Username));
    }

    // DELETE /api/sessions -> 204, clears the cookie.
    [Authorize]
    [HttpDelete]
    public async Task<IActionResult> SignOutSession()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

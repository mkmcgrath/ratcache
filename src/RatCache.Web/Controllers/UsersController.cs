using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RatCache.Web.Services;

namespace RatCache.Web.Controllers;

public record RegisterRequest(string? Username, string? Password);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public record DeleteAccountRequest(string? Password);
public record UserResponse(string Username, DateTime CreatedAt);

// FR-1.1, FR-1.2, FR-1.5, FR-1.6.
[ApiController]
[Route("api/users")]
public class UsersController(AccountService accounts) : ControllerBase
{
    // POST /api/users  {username, password} -> 201. Doesn't sign in; call POST /api/sessions next.
    [HttpPost]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var result = await accounts.RegisterAsync(req.Username, req.Password);
        if (!result.Ok) return ApiErrors.From(result.Error!);
        return StatusCode(StatusCodes.Status201Created, new UserResponse(result.Value!.Username, result.Value.CreatedAt));
    }

    // PUT /api/users/me/password  {currentPassword, newPassword} -> 204.
    // Signs out other browser sessions; this one stays signed in.
    [Authorize]
    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var result = await accounts.ChangePasswordAsync(User.GetUserId()!.Value, req.CurrentPassword, req.NewPassword);
        if (!result.Ok) return ApiErrors.From(result.Error!);

        if (User.Identity?.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme)
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                AuthPrincipal.Create(result.Value!, CookieAuthenticationDefaults.AuthenticationScheme),
                new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }

    // DELETE /api/users/me  {password} -> 204. Deletes the account, its pastes and its tokens.
    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> Delete(DeleteAccountRequest req)
    {
        var result = await accounts.DeleteAccountAsync(User.GetUserId()!.Value, req.Password);
        if (!result.Ok) return ApiErrors.From(result.Error!);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

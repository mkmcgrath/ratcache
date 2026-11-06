using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RatCache.Web.Services;

namespace RatCache.Web.Controllers;

public record CreateTokenRequest(string? Name);
public record TokenResponse(int Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);
public record NewTokenResponse(int Id, string Name, DateTime CreatedAt, string Token);

// FR-1.8: CLI API tokens. Send as "Authorization: Bearer <token>".
[ApiController]
[Authorize]
[Route("api/tokens")]
public class TokensController(ApiTokenService tokens) : ControllerBase
{
    // GET /api/tokens -> the signed-in user's tokens (never the secret itself).
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await tokens.ListAsync(User.GetUserId()!.Value);
        return Ok(list.Select(t => new TokenResponse(t.Id, t.Name, t.CreatedAt, t.LastUsedAt)));
    }

    // POST /api/tokens  {name?} -> 201 {id, name, createdAt, token}. The token is only shown here.
    [HttpPost]
    public async Task<IActionResult> Create(CreateTokenRequest? req)
    {
        var result = await tokens.CreateAsync(User.GetUserId()!.Value, req?.Name);
        if (!result.Ok) return ApiErrors.From(result.Error!);

        var t = result.Value!;
        return StatusCode(StatusCodes.Status201Created,
            new NewTokenResponse(t.Info.Id, t.Info.Name, t.Info.CreatedAt, t.Token));
    }

    // DELETE /api/tokens/{id} -> 204.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id)
    {
        var result = await tokens.RevokeAsync(User.GetUserId()!.Value, id);
        return result.Ok ? NoContent() : ApiErrors.From(result.Error!);
    }
}

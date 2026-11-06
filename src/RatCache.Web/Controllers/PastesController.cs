using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RatCache.Web.Models;
using RatCache.Web.Services;

namespace RatCache.Web.Controllers;

public record CreatePasteRequest(string? Content, string? Title, string? Language, string? Visibility, string? Expire);
public record EditPasteRequest(string? Content, string? Title, string? Language, string? Visibility, string? Expire);

public record PasteResponse(string Id, string Url, string RawUrl, string? Title, string Content, string Language,
    string Visibility, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? ExpiresAt, bool IsOwner);

public record PasteSummaryResponse(string Id, string Url, string RawUrl, string? Title, string Language,
    string Visibility, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? ExpiresAt);

// FR-2.1 – FR-2.7 over HTTP (SRS §5.2). Logic lives in PasteService.
[ApiController]
[Route("api/pastes")]
public class PastesController(PasteService pastes) : ControllerBase
{
    // POST /api/pastes -> 201 with the paste's link. Anyone can create; private needs sign-in.
    [HttpPost]
    public async Task<IActionResult> Create(CreatePasteRequest req)
    {
        var input = new NewPaste(req.Content, req.Title, req.Language, req.Visibility, req.Expire);
        var result = await pastes.CreateAsync(input, User.GetUserId());
        if (!result.Ok) return ApiErrors.From(result.Error!);

        var p = result.Value!;
        return Created($"/api/pastes/{p.Id}", Summary(p.Id, p.Title, p.Language, p.Visibility,
            p.CreatedAt, p.UpdatedAt, p.ExpiresAt));
    }

    // GET /api/pastes/{id} -> the paste and its metadata. 404 if missing, expired, or private.
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var result = await pastes.GetAsync(id, User.GetUserId());
        return result.Ok ? Ok(Full(result.Value!)) : ApiErrors.From(result.Error!);
    }

    // GET /api/pastes -> the signed-in user's pastes, newest first (no content).
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await pastes.ListForUserAsync(User.GetUserId()!.Value);
        return Ok(list.Select(p => Summary(p.Id, p.Title, p.Language, p.Visibility,
            p.CreatedAt, p.UpdatedAt, p.ExpiresAt)));
    }

    // PATCH /api/pastes/{id} -> 200 with the updated paste. Send only the fields to change.
    [Authorize]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Edit(string id, EditPasteRequest req)
    {
        var edit = new PasteEdit(req.Content, req.Title, req.Language, req.Visibility, req.Expire);
        var result = await pastes.UpdateAsync(id, edit, User.GetUserId()!.Value);
        return result.Ok ? Ok(Full(result.Value!)) : ApiErrors.From(result.Error!);
    }

    // DELETE /api/pastes/{id} -> 204. Owner or admin.
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await pastes.DeleteAsync(id, User.GetUserId()!.Value, User.IsAdmin());
        return result.Ok ? NoContent() : ApiErrors.From(result.Error!);
    }

    private PasteResponse Full(PasteView p) => new(p.Id, PageUrl(p.Id), RawUrl(p.Id), p.Title, p.Content,
        p.Language, Lower(p.Visibility), p.CreatedAt, p.UpdatedAt, p.ExpiresAt,
        IsOwner: p.OwnerId is not null && p.OwnerId == User.GetUserId());

    private PasteSummaryResponse Summary(string id, string? title, string language, Visibility visibility,
        DateTime createdAt, DateTime? updatedAt, DateTime? expiresAt) =>
        new(id, PageUrl(id), RawUrl(id), title, language, Lower(visibility), createdAt, updatedAt, expiresAt);

    // Absolute links so the CLI can print them (FR-6.1).
    private string PageUrl(string id) => $"{Request.Scheme}://{Request.Host}/p/{id}";
    private string RawUrl(string id) => $"{Request.Scheme}://{Request.Host}/raw/{id}";

    private static string Lower(Visibility v) => v.ToString().ToLowerInvariant();
}

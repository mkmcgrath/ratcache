using Microsoft.AspNetCore.Mvc;
using RatCache.Web.Services;

namespace RatCache.Web.Controllers;

// GET /raw/{id}: the paste's text as text/plain (FR-2.4). Same visibility and expiry rules as
// the paste page; missing, expired and private all return the same plain 404 (NFR-4.2).
[ApiController]
[Route("raw")]
public class RawController(PasteService pastes) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        // Browsers must never sniff this as HTML (NFR-3.3).
        Response.Headers.XContentTypeOptions = "nosniff";

        var result = await pastes.GetAsync(id, User.GetUserId());
        if (!result.Ok)
            return new ContentResult
            {
                StatusCode = StatusCodes.Status404NotFound,
                Content = "not found\n",
                ContentType = "text/plain; charset=utf-8",
            };
        return Content(result.Value!.Content, "text/plain; charset=utf-8");
    }
}

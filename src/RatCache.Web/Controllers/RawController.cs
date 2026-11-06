using Microsoft.AspNetCore.Mvc;

namespace RatCache.Web.Controllers;

// GET /raw/{id}: the paste's text as text/plain.
// TODO FR-2.4: look up the paste, check visibility and expiry, return Content(text, "text/plain").
[ApiController]
[Route("raw")]
public class RawController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult Get(string id) =>
        StatusCode(StatusCodes.Status501NotImplemented, "Not implemented yet (FR-2.4)\n");
}

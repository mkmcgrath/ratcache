using Microsoft.AspNetCore.Mvc;

namespace RatCache.Web.Controllers;

// Example controller: copy this pattern for new API endpoints (SRS §5.2).
// GET /api/health -> {"status":"ok"}. The CLI's `ratcache ping` calls this.
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}

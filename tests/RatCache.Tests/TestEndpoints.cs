using Microsoft.AspNetCore.Mvc;

namespace RatCache.Tests;

// Endpoints that only exist while testing (RatCacheFactory adds this assembly as an MVC
// application part). They always throw, to exercise the unhandled-error path (NFR-4.3, NFR-4.4).

public static class BoomMessages
{
    public const string Secret = "secret-internal-detail";
}

[ApiController]
[Route("api/test-boom")]
public class ApiBoomController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException(BoomMessages.Secret);
}

[ApiController]
[Route("raw/test-boom")]
public class RawBoomController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException(BoomMessages.Secret);
}
using Microsoft.AspNetCore.Mvc;
using RatCache.Web.Services;

namespace RatCache.Web.Controllers;

// NFR-4.1: every API error has this body. "error" is a stable snake_case code the CLI
// can switch on; "message" is plain language.
//     { "error": "paste_not_found", "message": "That paste doesn't exist or has expired." }
public record ApiErrorBody(string Error, string Message);

public static class ApiErrors
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Invalid => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
        _ => StatusCodes.Status500InternalServerError,
    };

    // In a controller: `return ApiErrors.From(result.Error);`
    public static IActionResult From(ServiceError error) =>
        new ObjectResult(new ApiErrorBody(error.Code, error.Message)) { StatusCode = StatusFor(error.Kind) };

    // For code outside MVC (auth handlers, middleware).
    public static Task WriteAsync(HttpResponse response, int status, string code, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new ApiErrorBody(code, message));
    }

    // Replaces [ApiController]'s default ProblemDetails for bad/malformed request bodies.
    // Registered in Program.cs (ConfigureApiBehaviorOptions).
    public static IActionResult FromModelState(ActionContext context)
    {
        var first = context.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .Select(kv => kv.Value!.Errors[0].ErrorMessage)
            .FirstOrDefault(m => !string.IsNullOrEmpty(m));
        // Don't echo parser exception details back (NFR-4.3); a generic message is enough.
        return new BadRequestObjectResult(new ApiErrorBody("invalid_request",
            first is not null && !first.Contains("Exception", StringComparison.Ordinal)
                ? first
                : "The request body is missing or isn't valid JSON."));
    }
}

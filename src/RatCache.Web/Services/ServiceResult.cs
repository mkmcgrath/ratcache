namespace RatCache.Web.Services;

// What went wrong, in terms a page or controller can turn into a response.
// Services don't know about HTTP; Controllers/ApiErrors.cs maps these to status codes.
public enum ErrorKind
{
    Invalid,        // 400
    Unauthorized,   // 401
    Forbidden,      // 403
    NotFound,       // 404
    Conflict,       // 409
    TooLarge,       // 413
}

// Code is a stable snake_case id (the API's "error" field); Message is plain language.
public record ServiceError(ErrorKind Kind, string Code, string Message);

public class ServiceResult
{
    public ServiceError? Error { get; protected init; }
    public bool Ok => Error is null;

    public static ServiceResult Success() => new();
    public static ServiceResult Fail(ErrorKind kind, string code, string message) =>
        new() { Error = new ServiceError(kind, code, message) };

    public static implicit operator ServiceResult(ServiceError error) => new() { Error = error };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; private init; }

    public static ServiceResult<T> Success(T value) => new() { Value = value };

    public static implicit operator ServiceResult<T>(T value) => Success(value);
    public static implicit operator ServiceResult<T>(ServiceError error) => new() { Error = error };
}

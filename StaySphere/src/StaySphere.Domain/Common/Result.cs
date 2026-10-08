namespace StaySphere.Domain.Common;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    PaymentRequired,
    TooManyRequests,
    Unavailable,
}

/// <summary>An expected, business-level failure. Exceptions are reserved for bugs and infrastructure faults.</summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    public static Result<T> Ok(T value) => new(value, null);
    public static new Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

namespace RdpManager.Application.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unreachable,
    NeedsReconfiguration,
    Cancelled,
    Unexpected,
}

public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    public static Error NotFound(string what) => new(ErrorKind.NotFound, "not_found", $"{what} was not found.");
    public static Error Validation(string message) => new(ErrorKind.Validation, "validation", message);
    public static Error Unreachable(string host) => new(ErrorKind.Unreachable, "unreachable", $"{host} is not reachable.");
    public static Error NeedsReconfiguration(string message) => new(ErrorKind.NeedsReconfiguration, "needs_reconfig", message);
    public static Error Cancelled() => new(ErrorKind.Cancelled, "cancelled", "The operation was cancelled.");
    public static Error Unexpected(string message) => new(ErrorKind.Unexpected, "unexpected", message);
}

/// <summary>
/// A minimal result type so services return failures as data, not exceptions.
/// Exceptions are reserved for programmer errors and truly exceptional I/O faults.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(T value) { _value = value; Error = null; }
    private Result(Error error) { _value = default; Error = error; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result is a failure: {Error!.Code}");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error!);
}

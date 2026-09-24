namespace Banking.Application.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    public static Error Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static Error NotFound(string resource) => new(ErrorKind.NotFound, "not_found", $"{resource} não encontrado.");

    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);
}

public readonly record struct Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static implicit operator Result<T>(T value) => new(value, null);

    /// <summary>Para quando o valor é interface ou coleção e a conversão implícita não se aplica.</summary>
    public static Result<T> From(T value) => new(value, null);

    public static implicit operator Result<T>(Error error) => new(default, error);
}

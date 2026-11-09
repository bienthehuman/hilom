using System.Diagnostics.CodeAnalysis;

namespace Hilom.SharedKernel.Results;

/// <summary>Outcome of an operation that can fail in an expected way. Exceptions stay for the unexpected.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(Error error) => new(default, error);
}

/// <summary>Outcome carrying a value on success. Create with <see cref="Result.Success{T}(T)"/>.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error)
        : base(error) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");
}

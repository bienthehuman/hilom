namespace Hilom.SharedKernel.Results;

public enum ErrorType
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    BusinessRule,
}

/// <summary>
/// An expected failure returned from a handler. <see cref="Code"/> is a stable, machine-readable
/// identifier (e.g. <c>slot_already_booked</c>) that the frontend can rely on.
/// </summary>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(code, message, ErrorType.Validation, fieldErrors);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
}

using Hilom.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Hilom.Infrastructure.Web;

/// <summary>Turns a handler's <see cref="Error"/> into a ProblemDetails response with a stable <c>code</c>.</summary>
public static class ErrorResults
{
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error.Type == ErrorType.Validation)
        {
            var fieldErrors = error.FieldErrors?.ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? new Dictionary<string, string[]>();
            return TypedResults.ValidationProblem(fieldErrors, detail: error.Message, extensions: extensions);
        }

        return TypedResults.Problem(
            detail: error.Message,
            statusCode: StatusCodeFor(error.Type),
            extensions: extensions);
    }

    /// <summary>For errors that aren't validation errors, typed for <c>Results&lt;…&gt;</c> unions.</summary>
    public static ProblemHttpResult ToProblemResult(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return TypedResults.Problem(
            detail: error.Message,
            statusCode: StatusCodeFor(error.Type),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    /// <summary>For validation errors, with per-field messages.</summary>
    public static ValidationProblem ToValidationResult(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return TypedResults.ValidationProblem(
            error.FieldErrors?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, string[]>(),
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}

using Hilom.SharedKernel.Results;

namespace Hilom.Api.ErrorHandling;

/// <summary>Turns a handler's <see cref="Error"/> into a ProblemDetails response with a stable <c>code</c>.</summary>
internal static class ErrorResults
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

    internal static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}

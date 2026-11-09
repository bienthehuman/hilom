using Hilom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hilom.Api.ErrorHandling;

/// <summary>
/// Returns 409/422 ProblemDetails when a database rule rejects a change (duplicate, overlap, stale version),
/// instead of a generic 500. Features still validate first to give friendlier messages.
/// </summary>
internal sealed class DatabaseExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment environment)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var violation = DatabaseErrors.Classify(exception);
        if (violation is null)
        {
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = violation.StatusCode,
            Title = violation.StatusCode == StatusCodes.Status409Conflict
                ? "The request conflicts with existing data."
                : "A value was rejected by the database.",
        };
        problem.Extensions["code"] = violation.Code;

        if (!environment.IsProduction() && violation.ConstraintName is not null)
        {
            problem.Extensions["constraint"] = violation.ConstraintName;
        }

        httpContext.Response.StatusCode = violation.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}

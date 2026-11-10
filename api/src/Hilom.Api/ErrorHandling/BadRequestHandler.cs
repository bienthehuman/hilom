using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hilom.Api.ErrorHandling;

/// <summary>
/// A request ASP.NET couldn't bind (e.g. <c>?kind=banana</c>). Program.cs makes the framework throw these in every
/// environment; answer with the status it chose (usually 400) as a problem with a <c>code</c>, like other errors.
/// </summary>
internal sealed class BadRequestHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        var problem = new ProblemDetails { Status = badRequest.StatusCode, Title = "The request is malformed.", Detail = badRequest.Message };
        problem.Extensions["code"] = "bad_request";

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}

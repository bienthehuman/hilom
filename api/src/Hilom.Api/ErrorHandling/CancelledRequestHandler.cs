using Microsoft.AspNetCore.Diagnostics;

namespace Hilom.Api.ErrorHandling;

/// <summary>
/// The browser gave up on the request (navigated away, closed the tab). That's not a server error:
/// answer 499 (client closed request) so logs and dashboards don't count it as one.
/// </summary>
internal sealed class CancelledRequestHandler : IExceptionHandler
{
    public const int ClientClosedRequest = 499;

    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not OperationCanceledException || !httpContext.RequestAborted.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        httpContext.Response.StatusCode = ClientClosedRequest;
        return ValueTask.FromResult(true);
    }
}

using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Hilom.Infrastructure.Auditing;

/// <summary>Who did it, from where, as part of which request.</summary>
public sealed record AuditRequestInfo(Guid? UserId, string CorrelationId, IPAddress? IpAddress, string? UserAgent)
{
    private const int MaxUserAgentLength = 500;

    /// <summary>Outside a request (startup seeding, background jobs) the actor is the system.</summary>
    public static readonly AuditRequestInfo System = new(null, "system", null, null);

    public static AuditRequestInfo From(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return System;
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return new AuditRequestInfo(
            Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
            CorrelationIdOf(httpContext),
            httpContext.Connection.RemoteIpAddress,
            string.IsNullOrEmpty(userAgent) ? null : userAgent[..Math.Min(userAgent.Length, MaxUserAgentLength)]);
    }

    /// <summary>Same id the API puts in ProblemDetails <c>traceId</c>, so an error report leads to its audit entries.</summary>
    public static string CorrelationIdOf(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;
}

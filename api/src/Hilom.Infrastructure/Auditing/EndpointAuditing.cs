using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hilom.Infrastructure.Auditing;

/// <summary>The audit events a state-changing endpoint may record on success. Read by the audit-coverage test.</summary>
public sealed record AuditedMetadata(IReadOnlyList<AuditEvent> Events);

/// <summary>A state-changing endpoint that deliberately records no event, and why.</summary>
public sealed record NotAuditedMetadata(string Reason);

public static class EndpointAuditing
{
    /// <summary>
    /// Declares the event(s) this endpoint records when it succeeds; at least one must be recorded via
    /// <see cref="IAuditLog"/>. If a successful response goes out without one, development and test runs fail
    /// loudly (production logs an error).
    /// </summary>
    public static RouteHandlerBuilder Audited(this RouteHandlerBuilder builder, params AuditEvent[] auditEvents)
    {
        ArgumentNullException.ThrowIfNull(auditEvents);
        if (auditEvents.Length == 0)
        {
            throw new ArgumentException("Declare at least one audit event.", nameof(auditEvents));
        }

        return builder
            .WithMetadata(new AuditedMetadata(auditEvents))
            .AddEndpointFilter(new AuditVerificationFilter(auditEvents));
    }

    /// <summary>Opts a state-changing endpoint out of auditing. The reason is part of the code review.</summary>
    public static TBuilder NotAudited<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return builder.WithMetadata(new NotAuditedMetadata(reason));
    }
}

internal sealed partial class AuditVerificationFilter(IReadOnlyList<AuditEvent> auditEvents) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        // Handlers return unions like Results<Created<T>, ProblemHttpResult>; the status is on the inner result.
        var inner = result;
        while (inner is INestedHttpResult nested)
        {
            inner = nested.Result;
        }

        var status = (inner as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (status is < 200 or > 299)
        {
            return result;
        }

        var services = context.HttpContext.RequestServices;
        var auditLog = services.GetRequiredService<IAuditLog>();
        if (auditLog.DeclaredNothingToRecord || auditLog.RecordedEvents.Any(auditEvents.Contains))
        {
            return result;
        }

        var message = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path} declares audit event(s) " +
            $"'{string.Join("', '", auditEvents.Select(e => e.Key))}' but succeeded without recording one.";
        if (!services.GetRequiredService<IHostEnvironment>().IsProduction())
        {
            throw new InvalidOperationException(message);
        }

        LogMissingAudit(services.GetRequiredService<ILogger<AuditVerificationFilter>>(), message);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
    private static partial void LogMissingAudit(ILogger logger, string message);
}

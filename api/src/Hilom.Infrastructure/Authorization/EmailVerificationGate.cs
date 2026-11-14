using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Infrastructure.Authorization;

/// <summary>Marks an endpoint that a self-registered account may call only after confirming its email.</summary>
public sealed record RequiresVerifiedEmailMetadata;

public static class EmailVerificationGateEndpoints
{
    /// <summary>
    /// Accounts that haven't confirmed their email get 403 <c>email_not_verified</c> here. Use it where an
    /// unconfirmed address would cost staff time or mislead them (asking to be linked to a patient).
    /// </summary>
    public static TBuilder RequiresVerifiedEmail<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequiresVerifiedEmailMetadata());
}

/// <summary>Holds back accounts whose email isn't confirmed from the endpoints that need it.</summary>
internal static class EmailVerificationGate
{
    public const string Code = "email_not_verified";

    public static bool Blocks(HttpContext context) =>
        context.User.HasClaim(c => c.Type == SessionClaims.EmailUnverified)
        && context.GetEndpoint()?.Metadata.GetMetadata<RequiresVerifiedEmailMetadata>() is not null;

    public static async Task RefuseAsync(HttpContext context)
    {
        await context.RequestServices.GetRequiredService<IAuditLog>().RecordNowAsync(
            AuditEvents.Access.Denied,
            new AuditOptions
            {
                Outcome = AuditOutcome.Denied,
                Details = new { reason = Code, method = context.Request.Method, path = context.Request.Path.Value },
            },
            context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Confirm your email first",
                Detail = "Enter the code we emailed you, then try again.",
                Extensions = { ["code"] = Code },
            },
        });
    }
}

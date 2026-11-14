using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Infrastructure.Authorization;

/// <summary>Marks an endpoint a user may call while they still hold a temporary password.</summary>
public sealed record AllowedBeforePasswordChangeMetadata;

public static class PasswordChangeGateEndpoints
{
    /// <summary>
    /// Lets users who must change their temporary password call this endpoint. Everything else answers
    /// 403 <c>password_change_required</c> until they do. Keep it to the few endpoints the change itself needs.
    /// </summary>
    public static TBuilder AllowedBeforePasswordChange<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowedBeforePasswordChangeMetadata());
}

/// <summary>
/// Holds back a signed-in user whose password was set by an administrator. Runs after authorization succeeds,
/// so it covers permission endpoints and signed-in-only endpoints alike. Anonymous endpoints (sign-in,
/// sign-out) skip authorization and stay open.
/// </summary>
internal static class PasswordChangeGate
{
    public const string Code = "password_change_required";

    public static bool Blocks(HttpContext context) =>
        context.User.HasClaim(c => c.Type == SessionClaims.PasswordChangeRequired)
        && context.GetEndpoint()?.Metadata.GetMetadata<AllowedBeforePasswordChangeMetadata>() is null;

    public static async Task RefuseAsync(HttpContext context)
    {
        await context.RequestServices.GetRequiredService<IAuditLog>().RecordNowAsync(
            AuditEvents.Access.Denied,
            new AuditOptions
            {
                Outcome = AuditOutcome.Denied,
                Details = new
                {
                    reason = Code,
                    method = context.Request.Method,
                    path = context.Request.Path.Value,
                },
            },
            context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Choose a new password first",
                Detail = "Your password was set by an administrator. Choose your own password to continue.",
                Extensions = { ["code"] = Code },
            },
        });
    }
}

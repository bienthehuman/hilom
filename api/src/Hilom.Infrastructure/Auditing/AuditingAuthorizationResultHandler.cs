using Hilom.Infrastructure.Authorization;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Infrastructure.Auditing;

/// <summary>
/// Records <c>access.denied</c> whenever a signed-in user is refused by a permission check (docs/AUDIT.md, rule 3),
/// then lets the default handler send the 403. Also holds back users who must still replace a temporary
/// password (<see cref="PasswordChangeGate"/>), and unconfirmed emails from the endpoints that need one
/// (<see cref="EmailVerificationGate"/>).
/// </summary>
internal sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var permissions = policy.Requirements.OfType<PermissionRequirement>().Select(r => r.PermissionKey).ToList();
            await context.RequestServices.GetRequiredService<IAuditLog>().RecordNowAsync(
                AuditEvents.Access.Denied,
                new AuditOptions
                {
                    Outcome = AuditOutcome.Denied,
                    Details = new
                    {
                        reason = "missing_permission",
                        permissions,
                        method = context.Request.Method,
                        path = context.Request.Path.Value,
                    },
                },
                context.RequestAborted);
        }

        else if (authorizeResult.Succeeded && PasswordChangeGate.Blocks(context))
        {
            await PasswordChangeGate.RefuseAsync(context);
            return;
        }
        else if (authorizeResult.Succeeded && EmailVerificationGate.Blocks(context))
        {
            await EmailVerificationGate.RefuseAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

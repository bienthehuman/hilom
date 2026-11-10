using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Infrastructure.Authorization;

public static class ScopeDenial
{
    /// <summary>
    /// Logs <c>access.denied</c> for a record that exists but is outside the user's scope. The API answers 404,
    /// so the caller can't tell it exists, but the attempt is still on record (docs/AUDIT.md, rule 3).
    /// Written immediately, since the request itself changes nothing.
    /// </summary>
    public static Task RecordOutOfScopeAsync(
        this IAuditLog auditLog,
        Permission permission,
        AuditTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(permission);
        return auditLog.RecordNowAsync(
            AuditEvents.Access.Denied,
            new AuditOptions
            {
                Outcome = AuditOutcome.Denied,
                Target = target,
                Details = new { reason = "out_of_scope", permissions = new[] { permission.Key } },
            },
            cancellationToken);
    }
}

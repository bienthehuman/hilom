using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features;

/// <summary>
/// Keeps portal and staff access apart (docs/RBAC.md, "Guardrails"): portal accounts hold only the portal role,
/// and the portal role goes only to portal accounts. So a portal user never gets staff permissions, even by mistake.
/// </summary>
internal static class PortalRoleGuard
{
    public static Task<Guid?> PortalRoleIdAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Set<Role>().Where(r => r.IsPortal).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Null when <paramref name="roleIds"/> may be held by an account of this type.</summary>
    public static async Task<Error?> CheckAsync(
        AppDbContext db,
        AccountType accountType,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var portalRoleId = await PortalRoleIdAsync(db, cancellationToken);
        return accountType switch
        {
            AccountType.Staff when portalRoleId is { } id && roleIds.Contains(id) => AccessErrors.PortalRoleIsForPortalAccounts,
            AccountType.Portal when roleIds.Any(r => r != portalRoleId) => AccessErrors.PortalAccountsHoldOnlyThePortalRole,
            _ => null,
        };
    }
}

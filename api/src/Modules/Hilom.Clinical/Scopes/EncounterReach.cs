using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Authorization;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Scopes;

namespace Hilom.Clinical.Scopes;

/// <summary>
/// Which encounters a permission reaches (docs/RBAC.md): <c>all</c>, or <c>own</c> = encounters where the
/// signed-in physician is the attending physician. <c>ward</c> (patients in a bed on my wards) comes with
/// admissions in slice 3.4, and <c>linked</c> (the portal-safe view) in 3.9; until then they reach no encounters.
/// </summary>
internal sealed record EncounterReach(bool All, Guid? PhysicianId)
{
    public static async Task<EncounterReach> ResolveAsync(
        ICurrentPermissions permissions,
        IStaffScope staff,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(permission, cancellationToken);
        if (scope.IsAll)
        {
            return new EncounterReach(true, null);
        }

        var physicianId = scope.Includes(PermissionScope.Own) ? await staff.MyEmployeeIdAsync(cancellationToken) : null;
        return new EncounterReach(false, physicianId);
    }

    public IQueryable<Encounter> Filter(IQueryable<Encounter> encounters)
    {
        if (All)
        {
            return encounters;
        }

        var physicianId = PhysicianId;
        return encounters.Where(e => physicianId != null && e.AttendingPhysicianId == physicianId);
    }
}

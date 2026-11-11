using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Scopes;

namespace Hilom.Clinical.Scopes;

/// <summary>
/// Which encounters a permission reaches (docs/RBAC.md): <c>all</c>; <c>own</c> = encounters where the
/// signed-in physician is the attending physician; <c>ward</c> = ward stays whose patient is in a bed on one of
/// the wards the user is assigned to today. <c>linked</c> (the portal-safe view) comes in 3.9; until then it
/// reaches no encounters. Scopes combine as a union.
/// </summary>
internal sealed record EncounterReach(bool All, Guid? PhysicianId, IReadOnlyList<Guid> WardIds, IQueryable<BedAssignment> Beds)
{
    public static async Task<EncounterReach> ResolveAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var beds = db.Set<BedAssignment>();
        var scope = await permissions.ScopeForAsync(permission, cancellationToken);
        if (scope.IsAll)
        {
            return new EncounterReach(true, null, [], beds);
        }

        var physicianId = scope.Includes(PermissionScope.Own) ? await staff.MyEmployeeIdAsync(cancellationToken) : null;
        var wardIds = scope.Includes(PermissionScope.Ward) ? await staff.MyWardIdsTodayAsync(cancellationToken) : [];
        return new EncounterReach(false, physicianId, wardIds, beds);
    }

    public IQueryable<Encounter> Filter(IQueryable<Encounter> encounters)
    {
        if (All)
        {
            return encounters;
        }

        var physicianId = PhysicianId;
        if (WardIds.Count == 0)
        {
            return encounters.Where(e => physicianId != null && e.AttendingPhysicianId == physicianId);
        }

        // The bed the patient is in now: an open bed assignment of the stay (the admission shares the encounter's id).
        var wardIds = WardIds;
        var beds = Beds;
        return encounters.Where(e =>
            (physicianId != null && e.AttendingPhysicianId == physicianId)
            || beds.Any(b => b.AdmissionId == e.Id && b.During.UpperBoundInfinite && wardIds.Contains(b.WardId)));
    }
}

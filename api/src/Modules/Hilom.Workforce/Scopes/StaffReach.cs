using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Scopes;

/// <summary>
/// What a permission's scope reaches for ward-based staff records (assignments, roster entries), per
/// docs/RBAC.md: <c>all</c>; <c>ward</c> = records on the wards the user is on today; <c>own</c> = the user's
/// own records. Ward and own combine as a union.
/// </summary>
internal sealed record StaffReach(bool All, IReadOnlyList<Guid> WardIds, Guid? EmployeeId)
{
    public static async Task<StaffReach> ResolveAsync(AccessScope scope, IStaffScope staff, CancellationToken cancellationToken)
    {
        if (scope.IsAll)
        {
            return new StaffReach(true, [], null);
        }

        var wards = scope.Includes(PermissionScope.Ward) ? await staff.MyWardIdsTodayAsync(cancellationToken) : [];
        var employee = scope.Includes(PermissionScope.Own) ? await staff.MyEmployeeIdAsync(cancellationToken) : null;
        return new StaffReach(false, wards, employee);
    }

    /// <summary>Everything on the ward (all, or the ward is one of mine today). Own records alone don't count.</summary>
    public bool ReachesWard(Guid wardId) => All || WardIds.Contains(wardId);

    public bool Reaches(Guid wardId, Guid employeeId) =>
        ReachesWard(wardId) || (EmployeeId is { } id && employeeId == id);

    public IQueryable<WardAssignment> Filter(IQueryable<WardAssignment> assignments)
    {
        if (All)
        {
            return assignments;
        }

        var wardIds = WardIds;
        var employeeId = EmployeeId;
        return assignments.Where(a => wardIds.Contains(a.WardId) || (employeeId != null && a.EmployeeId == employeeId));
    }

    public IQueryable<RosterEntry> Filter(IQueryable<RosterEntry> entries)
    {
        if (All)
        {
            return entries;
        }

        var wardIds = WardIds;
        var employeeId = EmployeeId;
        return entries.Where(r => wardIds.Contains(r.WardId) || (employeeId != null && r.EmployeeId == employeeId));
    }
}

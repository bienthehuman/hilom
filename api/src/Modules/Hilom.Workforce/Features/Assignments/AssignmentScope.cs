using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;

namespace Hilom.Workforce.Features.Assignments;

/// <summary>
/// Which ward assignments a scope reaches (docs/RBAC.md): <c>all</c>; <c>ward</c> = assignments to the wards
/// the user is on today; <c>own</c> = the user's own assignments. Ward and own combine as a union.
/// </summary>
internal sealed record AssignmentReach(bool All, IReadOnlyList<Guid> WardIds, Guid? EmployeeId)
{
    public static async Task<AssignmentReach> ResolveAsync(AccessScope scope, IStaffScope staff, CancellationToken cancellationToken)
    {
        if (scope.IsAll)
        {
            return new AssignmentReach(true, [], null);
        }

        var wards = scope.Includes(PermissionScope.Ward) ? await staff.MyWardIdsTodayAsync(cancellationToken) : [];
        var employee = scope.Includes(PermissionScope.Own) ? await staff.MyEmployeeIdAsync(cancellationToken) : null;
        return new AssignmentReach(false, wards, employee);
    }

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

    public bool Reaches(WardAssignment assignment) =>
        All || WardIds.Contains(assignment.WardId) || (EmployeeId is { } id && assignment.EmployeeId == id);

    /// <summary>For changes: may the user manage assignments to this ward?</summary>
    public bool ReachesWard(Guid wardId) => All || WardIds.Contains(wardId);
}

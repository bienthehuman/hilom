using Hilom.Patients.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Patients.Features.Registry;

/// <summary>
/// Which patients a scope reaches (docs/RBAC.md). <c>all</c> reaches everyone. <c>linked</c> reaches patients
/// the account has an active, verified link to; seeing records also needs the link's <c>can_view_records</c>.
/// <c>own</c> (a physician's appointments and encounters, 2.6 / phase 3) and <c>ward</c> (bed assignments,
/// phase 3) reach no one until those records exist.
/// </summary>
internal static class PatientScope
{
    public static IQueryable<Patient> WhereInScope(
        this IQueryable<Patient> patients,
        AccessScope scope,
        IQueryable<PatientAccountLink> links)
    {
        if (scope.IsAll)
        {
            return patients;
        }

        if (scope.Includes(PermissionScope.Linked) && scope.UserId is { } userId)
        {
            var viewable = links.ViewableBy(userId).Select(l => l.PatientId);
            return patients.Where(p => viewable.Contains(p.Id));
        }

        return patients.Where(_ => false);
    }

    /// <summary>Active links through which the account may see the patient's records.</summary>
    public static IQueryable<PatientAccountLink> ViewableBy(this IQueryable<PatientAccountLink> links, Guid userId) =>
        links.Where(l => l.UserId == userId && l.Status == LinkStatus.Active && l.CanViewRecords);
}

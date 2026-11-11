using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Features.Registry;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Scopes;

/// <summary>
/// A nurse's <c>ward</c> patients (docs/RBAC.md) are those in a bed now on a ward they're assigned to today.
/// Evaluated live, so a transfer or a new ward assignment changes access on the next request.
/// </summary>
internal sealed class WardPatientsThroughBedAssignments(AppDbContext db, HospitalClock clock) : IPatientReachSource
{
    public PermissionScope Scope => PermissionScope.Ward;

    public IQueryable<Guid> PatientIds(Guid userId)
    {
        var today = clock.Today;
        return from bed in db.Set<BedAssignment>()
               join admission in db.Set<Admission>() on bed.AdmissionId equals admission.Id
               join assignment in db.Set<WardAssignment>() on bed.WardId equals assignment.WardId
               join employee in db.Set<Employee>() on assignment.EmployeeId equals employee.Id
               where employee.UserId == userId && assignment.Period.Contains(today) && bed.During.UpperBoundInfinite
               select admission.PatientId;
    }
}

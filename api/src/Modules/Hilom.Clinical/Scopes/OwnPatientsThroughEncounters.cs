using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Features.Registry;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;

namespace Hilom.Clinical.Scopes;

/// <summary>A physician's <c>own</c> patients (docs/RBAC.md) include everyone they're the attending physician for.</summary>
internal sealed class OwnPatientsThroughEncounters(AppDbContext db) : IPatientReachSource
{
    public PermissionScope Scope => PermissionScope.Own;

    public IQueryable<Guid> PatientIds(Guid userId) =>
        from encounter in db.Set<Encounter>()
        join e in db.Set<Employee>() on encounter.AttendingPhysicianId equals e.Id
        where e.UserId == userId
        select encounter.PatientId;
}

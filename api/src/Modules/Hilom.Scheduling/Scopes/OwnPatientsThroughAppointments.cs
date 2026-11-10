using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Features.Registry;
using Hilom.Scheduling.Domain;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;

namespace Hilom.Scheduling.Scopes;

/// <summary>
/// A physician's <c>own</c> patients (docs/RBAC.md): patients with an appointment in one of their slots, so they can
/// open the chart before the visit. Declined and cancelled requests don't count.
/// </summary>
internal sealed class OwnPatientsThroughAppointments(AppDbContext db) : IPatientReachSource
{
    private static readonly AppointmentStatus[] Counted =
        [AppointmentStatus.Requested, AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn, AppointmentStatus.Completed, AppointmentStatus.NoShow];

    public PermissionScope Scope => PermissionScope.Own;

    public IQueryable<Guid> PatientIds(Guid userId) =>
        from a in db.Set<Appointment>()
        join s in db.Set<AvailabilitySlot>() on a.SlotId equals s.Id
        join e in db.Set<Employee>() on s.PhysicianId equals e.Id
        where e.UserId == userId && Counted.Contains(a.Status)
        select a.PatientId;
}

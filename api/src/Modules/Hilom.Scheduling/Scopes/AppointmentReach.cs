using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Scopes;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Scheduling.Scopes;

/// <summary>
/// Which appointments a permission reaches (docs/RBAC.md): <c>all</c>; <c>own</c> = appointments in the physician's
/// own slots; <c>linked</c> = appointments of patients the portal account is linked to. Seeing them needs an active
/// link; acting on them (cancelling) needs the link's <c>can_book</c>. Scopes combine as a union.
/// </summary>
internal sealed record AppointmentReach(bool All, Guid? PhysicianId, Guid? LinkedUserId, bool NeedsBooking)
{
    public static async Task<AppointmentReach> ResolveAsync(
        ICurrentPermissions permissions,
        IStaffScope staff,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(permission, cancellationToken);
        if (scope.IsAll)
        {
            return new AppointmentReach(true, null, null, false);
        }

        var physicianId = scope.Includes(PermissionScope.Own) ? await staff.MyEmployeeIdAsync(cancellationToken) : null;
        var linkedUser = scope.Includes(PermissionScope.Linked) ? scope.UserId : null;
        return new AppointmentReach(false, physicianId, linkedUser, NeedsBooking: permission != Perm.Scheduling.AppointmentsView);
    }

    public IQueryable<Appointment> Filter(AppDbContext db, IQueryable<Appointment> appointments)
    {
        if (All)
        {
            return appointments;
        }

        var physicianId = PhysicianId;
        var mySlots = db.Set<AvailabilitySlot>().Where(s => s.PhysicianId == physicianId).Select(s => s.Id);
        var myPatients = Links(db).Select(l => l.PatientId!.Value);
        var linked = LinkedUserId is not null;
        return appointments.Where(a =>
            (physicianId != null && mySlots.Contains(a.SlotId)) || (linked && myPatients.Contains(a.PatientId)));
    }

    public Task<bool> ReachesAsync(AppDbContext db, Guid appointmentId, CancellationToken cancellationToken) =>
        Filter(db, db.Set<Appointment>().Where(a => a.Id == appointmentId)).AnyAsync(cancellationToken);

    /// <summary>The link a portal user acts through for this patient, for the audit log. Null for staff.</summary>
    public Task<Guid?> LinkForAsync(AppDbContext db, Guid patientId, CancellationToken cancellationToken) =>
        LinkedUserId is null || All
            ? Task.FromResult<Guid?>(null)
            : Links(db).Where(l => l.PatientId == patientId).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<PatientAccountLink> Links(AppDbContext db)
    {
        var userId = LinkedUserId;
        var needsBooking = NeedsBooking;
        return db.Set<PatientAccountLink>().Where(l =>
            l.UserId == userId
            && l.Status == LinkStatus.Active
            && (needsBooking ? l.CanBook : l.CanBook || l.CanViewRecords));
    }
}

using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Hilom.Scheduling.Features.Booking;
using Hilom.Scheduling.Scopes;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Scheduling.Features.Appointments;

/// <summary>
/// The appointment workflow (docs/DATA_MODEL.md, appointments): lists for the front desk, physicians and the portal;
/// confirm and decline (<c>appointments.decide</c>), cancel (<c>appointments.cancel</c>) and no-show
/// (<c>appointments.check_in</c>, the front desk's job, like check-in).
/// </summary>
internal static class AppointmentEndpoints
{
    /// <summary>A staff list covers at most this many days.</summary>
    private const int MaxDays = 31;

    /// <summary>The portal shows past appointments this far back.</summary>
    private static readonly TimeSpan PortalHistory = TimeSpan.FromDays(180);

    /// <summary>Appointments in the caller's reach between two hospital dates, soonest first (day list, doctor inbox).</summary>
    public static async Task<Results<Ok<List<AppointmentResponse>>, ValidationProblem>> ListAppointments(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        HospitalClock clock,
        TimeProvider time,
        DateOnly? from,
        int? days,
        Guid? physicianId,
        AppointmentStatus? status,
        CancellationToken cancellationToken)
    {
        var start = from ?? clock.Today;
        var length = days ?? 1;
        if (length is < 1 or > MaxDays)
        {
            return SchedulingErrors.InvalidPattern("days", $"List 1 to {MaxDays} days at a time.").ToValidationResult();
        }

        var view = await AppointmentReach.ResolveAsync(permissions, staff, Perm.Scheduling.AppointmentsView, cancellationToken);
        var windowStart = StartOf(start, clock).UtcDateTime;
        var windowEnd = StartOf(start.AddDays(length), clock).UtcDateTime;
        var inWindow = db.Set<AvailabilitySlot>()
            .Where(s => s.During.LowerBound >= windowStart && s.During.LowerBound < windowEnd
                && (physicianId == null || s.PhysicianId == physicianId))
            .Select(s => s.Id);
        var query = view.Filter(db, db.Set<Appointment>().Where(a => inWindow.Contains(a.SlotId)
            && (status == null || a.Status == status)));

        var cancel = await AppointmentReach.ResolveAsync(permissions, staff, Perm.Scheduling.AppointmentsCancel, cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, query, cancel, time.GetUtcNow(), cancellationToken));
    }

    /// <summary>
    /// The portal: appointments of every patient linked to the account, upcoming and from the last 180 days.
    /// <c>canCancel</c> says which the account may cancel (needs the link's <c>can_book</c>).
    /// </summary>
    public static async Task<Ok<List<AppointmentResponse>>> ListMyAppointments(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = time.GetUtcNow();
        var since = (now - PortalHistory).UtcDateTime;
        var recent = db.Set<AvailabilitySlot>().Where(s => s.During.UpperBound > since).Select(s => s.Id);
        var view = new AppointmentReach(false, null, userId, NeedsBooking: false);
        var cancel = view with { NeedsBooking = true };

        var query = view.Filter(db, db.Set<Appointment>().Where(a => recent.Contains(a.SlotId)));
        return TypedResults.Ok(await ToResponsesAsync(db, query, cancel, now, cancellationToken));
    }

    public static Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> ConfirmAppointment(
        Guid id,
        ConfirmAppointmentRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ActAsync(
            id,
            request.Version,
            Perm.Scheduling.AppointmentsDecide,
            AuditEvents.Scheduling.AppointmentConfirmed,
            (appointment, slot, now) => appointment.Confirm(currentUser.UserId!.Value, now, slot.StartsAt, request.Note),
            appointment => new { note = appointment.DecisionNote },
            db,
            permissions,
            staff,
            auditLog,
            time,
            cancellationToken);

    public static Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> DeclineAppointment(
        Guid id,
        DeclineAppointmentRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ActAsync(
            id,
            request.Version,
            Perm.Scheduling.AppointmentsDecide,
            AuditEvents.Scheduling.AppointmentDeclined,
            (appointment, _, now) => appointment.Decline(currentUser.UserId!.Value, now, request.Reason),
            appointment => new { reason = appointment.DecisionNote },
            db,
            permissions,
            staff,
            auditLog,
            time,
            cancellationToken);

    public static Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> CancelAppointment(
        Guid id,
        CancelAppointmentRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ActAsync(
            id,
            request.Version,
            Perm.Scheduling.AppointmentsCancel,
            AuditEvents.Scheduling.AppointmentCancelled,
            (appointment, slot, now) => appointment.Cancel(currentUser.UserId!.Value, now, slot.StartsAt, request.Reason),
            appointment => new { reason = appointment.CancelReason },
            db,
            permissions,
            staff,
            auditLog,
            time,
            cancellationToken);

    public static Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> MarkNoShow(
        Guid id,
        NoShowRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ActAsync(
            id,
            request.Version,
            Perm.Scheduling.AppointmentsCheckIn,
            AuditEvents.Scheduling.AppointmentNoShow,
            (appointment, slot, now) => appointment.MarkNoShow(now, slot.StartsAt),
            _ => null,
            db,
            permissions,
            staff,
            auditLog,
            time,
            cancellationToken);

    /// <summary>
    /// Loads the appointment, checks the permission's scope (out of scope is 404 and <c>access.denied</c>), applies
    /// the change, records the event with the patient (and the link, for a portal user), and saves with the version.
    /// </summary>
    private static async Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> ActAsync(
        Guid id,
        uint version,
        Permission permission,
        AuditEvent auditEvent,
        Func<Appointment, AvailabilitySlot, DateTimeOffset, Result> change,
        Func<Appointment, object?> details,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (appointment is null)
        {
            return SchedulingErrors.AppointmentNotFound.ToProblemResult();
        }

        var reach = await AppointmentReach.ResolveAsync(permissions, staff, permission, cancellationToken);
        if (!await reach.ReachesAsync(db, appointment.Id, cancellationToken))
        {
            await auditLog.RecordOutOfScopeAsync(permission, Target(appointment), cancellationToken);
            return SchedulingErrors.AppointmentNotFound.ToProblemResult();
        }

        var slot = await db.Set<AvailabilitySlot>().AsNoTracking().SingleAsync(s => s.Id == appointment.SlotId, cancellationToken);
        var now = time.GetUtcNow();
        var changed = change(appointment, slot, now);
        if (!changed.IsSuccess)
        {
            return changed.Error.ToProblemResult();
        }

        auditLog.Record(
            auditEvent,
            new AuditOptions
            {
                Target = Target(appointment),
                PatientId = appointment.PatientId,
                ActingForPatientLinkId = await reach.LinkForAsync(db, appointment.PatientId, cancellationToken),
                Details = new { physicianId = slot.PhysicianId, startsAt = slot.StartsAt, more = details(appointment) },
            });

        var entry = db.Entry(appointment);
        entry.Property(a => a.Version).OriginalValue = version;
        entry.Property(a => a.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);

        var cancel = await AppointmentReach.ResolveAsync(permissions, staff, Perm.Scheduling.AppointmentsCancel, cancellationToken);
        var responses = await ToResponsesAsync(db, db.Set<Appointment>().Where(a => a.Id == appointment.Id), cancel, now, cancellationToken);
        return TypedResults.Ok(responses[0]);
    }

    /// <summary>Appointments as the API shows them, soonest first, with who booked and decided, and whether the caller may cancel.</summary>
    internal static async Task<List<AppointmentResponse>> ToResponsesAsync(
        AppDbContext db,
        IQueryable<Appointment> appointments,
        AppointmentReach cancelReach,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from a in appointments.AsNoTracking()
            join s in db.Set<AvailabilitySlot>() on a.SlotId equals s.Id
            join p in db.Set<Patient>() on a.PatientId equals p.Id
            join e in db.Set<Employee>() on s.PhysicianId equals e.Id
            join sp in db.Set<Specialization>() on e.Physician!.SpecializationId equals sp.Id
            orderby s.During.LowerBound
            select new
            {
                Appointment = a,
                s.During,
                Patient = new AppointmentPatientRef(p.Id, p.Mrn, p.FirstName, p.MiddleName, p.LastName),
                Physician = new BookingPhysicianResponse(e.Id, e.FirstName, e.LastName, sp.Id, sp.Name),
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Appointment.Id).ToList();
        var cancellable = cancelReach.All
            ? ids.ToHashSet()
            : (await cancelReach.Filter(db, db.Set<Appointment>().Where(a => ids.Contains(a.Id))).Select(a => a.Id).ToListAsync(cancellationToken)).ToHashSet();

        var userIds = rows
            .SelectMany(r => new[] { (Guid?)r.Appointment.BookedBy, r.Appointment.DecidedBy, r.Appointment.CancelledBy })
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var names = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        string? NameOf(Guid? userId) => userId is { } id && names.TryGetValue(id, out var name) ? name : null;

        return
        [
            .. rows.Select(r =>
            {
                var a = r.Appointment;
                var startsAt = new DateTimeOffset(r.During.LowerBound, TimeSpan.Zero);
                return new AppointmentResponse(
                    a.Id,
                    a.Status,
                    a.Reason,
                    startsAt,
                    new DateTimeOffset(r.During.UpperBound, TimeSpan.Zero),
                    r.Physician,
                    r.Patient,
                    a.CreatedAt,
                    NameOf(a.BookedBy),
                    NameOf(a.DecidedBy),
                    a.DecidedAt,
                    a.DecisionNote,
                    NameOf(a.CancelledBy),
                    a.CancelledAt,
                    a.CancelReason,
                    a.Status is AppointmentStatus.Requested or AppointmentStatus.Confirmed && startsAt > now && cancellable.Contains(a.Id),
                    a.Version);
            }),
        ];
    }

    private static DateTimeOffset StartOf(DateOnly day, HospitalClock clock) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.TimeZone), TimeSpan.Zero);

    private static AuditTarget Target(Appointment appointment) => new(nameof(Appointment), appointment.Id);
}

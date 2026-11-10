using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Hilom.Scheduling.Persistence;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Scheduling.Features.Booking;

/// <summary>
/// Finding a free slot and booking it. Staff book for any patient (<c>scheduling.appointments.book</c> at
/// <c>all</c>); portal users for patients they're linked to with <c>can_book</c> (<c>linked</c>).
/// </summary>
internal static class BookingEndpoints
{
    /// <summary>The slot search covers at most this many days.</summary>
    private const int MaxDays = 14;

    private const int MaxResults = 300;

    public static async Task<Ok<List<BookingPhysicianResponse>>> ListBookablePhysicians(
        AppDbContext db,
        Guid? specializationId,
        CancellationToken cancellationToken)
    {
        var physicians = await BookablePhysicians(db, specializationId: specializationId).ToListAsync(cancellationToken);
        return TypedResults.Ok(physicians);
    }

    /// <summary>Free slots from <paramref name="from"/> (hospital date) for <paramref name="days"/> days, soonest first.</summary>
    public static async Task<Results<Ok<List<OpenSlotResponse>>, ValidationProblem>> ListOpenSlots(
        AppDbContext db,
        HospitalClock clock,
        TimeProvider time,
        DateOnly? from,
        int? days,
        Guid? specializationId,
        Guid? physicianId,
        CancellationToken cancellationToken)
    {
        var start = from ?? clock.Today;
        var length = days ?? 7;
        if (length is < 1 or > MaxDays)
        {
            return SchedulingErrors.InvalidPattern("days", $"Search 1 to {MaxDays} days at a time.").ToValidationResult();
        }

        var now = time.GetUtcNow().UtcDateTime;
        var windowStart = StartOf(start, clock).UtcDateTime;
        var windowEnd = StartOf(start.AddDays(length), clock).UtcDateTime;
        var earliest = windowStart > now ? windowStart : now;

        var slots = await (
            from s in db.Set<AvailabilitySlot>().AsNoTracking()
            join e in db.Set<Employee>() on s.PhysicianId equals e.Id
            join sp in db.Set<Specialization>() on e.Physician!.SpecializationId equals sp.Id
            where s.Status == SlotStatus.Open
                && e.Physician!.AcceptsAppointments
                && e.Status != EmployeeStatus.Separated
                && s.During.LowerBound >= earliest
                && s.During.LowerBound < windowEnd
                && (specializationId == null || sp.Id == specializationId)
                && (physicianId == null || e.Id == physicianId)
                && !db.Set<Appointment>().Any(a => a.SlotId == s.Id && Appointment.Live.Contains(a.Status))
            orderby s.During.LowerBound, e.LastName
            select new
            {
                s.Id,
                Physician = new BookingPhysicianResponse(e.Id, e.FirstName, e.LastName, sp.Id, sp.Name),
                s.During,
            })
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(slots
            .Select(s => new OpenSlotResponse(
                s.Id,
                s.Physician,
                new DateTimeOffset(s.During.LowerBound, TimeSpan.Zero),
                new DateTimeOffset(s.During.UpperBound, TimeSpan.Zero)))
            .ToList());
    }

    public static async Task<Results<Created<AppointmentResponse>, ProblemHttpResult>> BookAppointment(
        BookAppointmentRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var patient = await db.Set<Patient>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken);
        if (patient is null)
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        // Staff book for anyone; a portal user only through an active link that allows booking.
        var scope = await permissions.ScopeForAsync(Perm.Scheduling.AppointmentsBook, cancellationToken);
        Guid? viaLink = null;
        if (!scope.IsAll)
        {
            viaLink = scope.Includes(PermissionScope.Linked)
                ? await db.Set<PatientAccountLink>()
                    .Where(l => l.UserId == userId && l.PatientId == patient.Id && l.Status == LinkStatus.Active && l.CanBook)
                    .Select(l => (Guid?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            if (viaLink is null)
            {
                await auditLog.RecordOutOfScopeAsync(Perm.Scheduling.AppointmentsBook, new AuditTarget(nameof(Patient), patient.Id), cancellationToken);
                return PatientErrors.PatientNotFound.ToProblemResult();
            }
        }

        var slot = await db.Set<AvailabilitySlot>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.SlotId, cancellationToken);
        if (slot is null)
        {
            return SchedulingErrors.SlotNotFound.ToProblemResult();
        }

        var physician = await BookablePhysicians(db, physicianId: slot.PhysicianId).SingleOrDefaultAsync(cancellationToken);
        if (slot.Status != SlotStatus.Open || slot.StartsAt <= time.GetUtcNow() || physician is null)
        {
            return SchedulingErrors.SlotNotBookable.ToProblemResult();
        }

        if (await db.Set<Appointment>().AnyAsync(a => a.SlotId == slot.Id && Appointment.Live.Contains(a.Status), cancellationToken))
        {
            return SchedulingErrors.SlotTaken.ToProblemResult();
        }

        var appointment = new Appointment(slot.Id, patient.Id, userId, request.Reason);
        db.Add(appointment);
        auditLog.Record(
            AuditEvents.Scheduling.AppointmentBooked,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Appointment), appointment.Id),
                PatientId = patient.Id,
                ActingForPatientLinkId = viaLink,
                Details = new
                {
                    mrn = patient.Mrn,
                    physicianId = slot.PhysicianId,
                    startsAt = slot.StartsAt,
                    forLinkedPatient = viaLink is not null,
                },
            });

        // Two people booking the same slot at once: the partial unique index lets one through.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: AppointmentConfiguration.OneLivePerSlot,
        })
        {
            return SchedulingErrors.SlotTaken.ToProblemResult();
        }

        var response = new AppointmentResponse(
            appointment.Id,
            appointment.Status,
            appointment.Reason,
            slot.StartsAt,
            slot.EndsAt,
            physician,
            new AppointmentPatientRef(patient.Id, patient.Mrn, patient.FirstName, patient.MiddleName, patient.LastName),
            appointment.CreatedAt);
        return TypedResults.Created($"/api/scheduling/appointments/{appointment.Id}", response);
    }

    /// <summary>
    /// Upcoming requested and confirmed appointments of the patients the portal user is linked to. The full list,
    /// history and cancelling come with slice 2.7.
    /// </summary>
    public static async Task<Ok<List<AppointmentResponse>>> ListMyAppointments(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = time.GetUtcNow().UtcDateTime;
        AppointmentStatus[] upcoming = [AppointmentStatus.Requested, AppointmentStatus.Confirmed];
        var linked = db.Set<PatientAccountLink>()
            .Where(l => l.UserId == userId && l.Status == LinkStatus.Active && (l.CanBook || l.CanViewRecords))
            .Select(l => l.PatientId!.Value);

        var rows = await (
            from a in db.Set<Appointment>().AsNoTracking()
            join s in db.Set<AvailabilitySlot>() on a.SlotId equals s.Id
            join p in db.Set<Patient>() on a.PatientId equals p.Id
            join e in db.Set<Employee>() on s.PhysicianId equals e.Id
            join sp in db.Set<Specialization>() on e.Physician!.SpecializationId equals sp.Id
            where linked.Contains(a.PatientId) && upcoming.Contains(a.Status) && s.During.UpperBound > now
            orderby s.During.LowerBound
            select new
            {
                Appointment = a,
                s.During,
                Patient = p,
                Physician = new BookingPhysicianResponse(e.Id, e.FirstName, e.LastName, sp.Id, sp.Name),
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(r => new AppointmentResponse(
                r.Appointment.Id,
                r.Appointment.Status,
                r.Appointment.Reason,
                new DateTimeOffset(r.During.LowerBound, TimeSpan.Zero),
                new DateTimeOffset(r.During.UpperBound, TimeSpan.Zero),
                r.Physician,
                new AppointmentPatientRef(r.Patient.Id, r.Patient.Mrn, r.Patient.FirstName, r.Patient.MiddleName, r.Patient.LastName),
                r.Appointment.CreatedAt))
            .ToList());
    }

    /// <summary>Physicians who take appointments and still work here. Filters apply before projecting, so EF can translate them.</summary>
    private static IQueryable<BookingPhysicianResponse> BookablePhysicians(
        AppDbContext db,
        Guid? physicianId = null,
        Guid? specializationId = null) =>
        from e in db.Set<Employee>()
        join s in db.Set<Specialization>() on e.Physician!.SpecializationId equals s.Id
        where e.Physician != null
            && e.Physician.AcceptsAppointments
            && e.Status != EmployeeStatus.Separated
            && (physicianId == null || e.Id == physicianId)
            && (specializationId == null || s.Id == specializationId)
        orderby e.LastName, e.FirstName
        select new BookingPhysicianResponse(e.Id, e.FirstName, e.LastName, s.Id, s.Name);

    private static DateTimeOffset StartOf(DateOnly day, HospitalClock clock) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.TimeZone), TimeSpan.Zero);
}

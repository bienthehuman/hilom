using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Scheduling.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Scheduling.Visits;

/// <summary>A checked-in appointment, with what Clinical needs to open the encounter.</summary>
/// <param name="PhysicianId">The slot's physician (employee id), who becomes the attending physician.</param>
/// <param name="Reason">The booking reason, which becomes the encounter's chief complaint.</param>
public sealed record CheckedInAppointment(Guid AppointmentId, Guid PatientId, Guid PhysicianId, string Reason, DateTimeOffset StartsAt);

/// <summary>
/// Appointment changes that come from the visit itself, for Clinical (which sits above Scheduling): checking in
/// opens the outpatient encounter, and closing that encounter completes the appointment. Scheduling still makes
/// the change to its own table (docs/ARCHITECTURE.md §3, rule 1). Changes are tracked but not saved, so the caller
/// saves them together with its own in one transaction.
/// </summary>
public interface IAppointmentVisits
{
    /// <summary>
    /// Marks a confirmed appointment as checked in and records <c>scheduling.appointment.checked_in</c>. Only on the
    /// appointment's day. <paramref name="version"/> is the appointment's version the caller last saw.
    /// </summary>
    Task<Result<CheckedInAppointment>> CheckInAsync(Guid appointmentId, uint version, CancellationToken cancellationToken);

    /// <summary>Marks a checked-in appointment as completed, when its encounter is closed.</summary>
    Task<Result> CompleteAsync(Guid appointmentId, CancellationToken cancellationToken);
}

internal sealed class AppointmentVisits(AppDbContext db, HospitalClock clock, IAuditLog auditLog) : IAppointmentVisits
{
    public async Task<Result<CheckedInAppointment>> CheckInAsync(Guid appointmentId, uint version, CancellationToken cancellationToken)
    {
        // Checking in needs scheduling.appointments.check_in, which the catalog only grants at `all`: no scope filter.
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<CheckedInAppointment>(SchedulingErrors.AppointmentNotFound);
        }

        var slot = await db.Set<AvailabilitySlot>().AsNoTracking().SingleAsync(s => s.Id == appointment.SlotId, cancellationToken);
        var appointmentDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(slot.StartsAt, clock.TimeZone).DateTime);
        var checkedIn = appointment.CheckIn(clock.Today, appointmentDay);
        if (!checkedIn.IsSuccess)
        {
            return Result.Failure<CheckedInAppointment>(checkedIn.Error);
        }

        var entry = db.Entry(appointment);
        entry.Property(a => a.Version).OriginalValue = version;
        auditLog.Record(
            AuditEvents.Scheduling.AppointmentCheckedIn,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Appointment), appointment.Id),
                PatientId = appointment.PatientId,
                Details = new { physicianId = slot.PhysicianId, startsAt = slot.StartsAt },
            });

        return Result.Success(new CheckedInAppointment(appointment.Id, appointment.PatientId, slot.PhysicianId, appointment.Reason, slot.StartsAt));
    }

    public async Task<Result> CompleteAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().SingleAsync(a => a.Id == appointmentId, cancellationToken);
        return appointment.Complete();
    }
}

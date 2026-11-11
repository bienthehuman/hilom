using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Vitals;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.Clinical.Features.PortalRecords;

/// <summary>
/// The portal's view of a linked patient's records (docs/RBAC.md, <c>linked</c> on <c>clinical.encounters.view</c>):
/// visit and discharge summaries, vital signs and appointments. It is its own endpoint, not a scope on the staff
/// encounter endpoints, so nursing notes, orders and the medication record are unreachable from the portal by
/// construction. It answers only through an active link with <c>can_view_records</c>.
/// </summary>
internal static class PortalRecordsEndpoints
{
    /// <summary>Past appointments and vitals the portal shows reach this far back.</summary>
    private const int MaxVitals = 100;

    private static readonly TimeSpan AppointmentHistory = TimeSpan.FromDays(365);

    public static async Task<Results<Ok<PortalRecordsResponse>, ProblemHttpResult>> GetRecords(
        Guid patientId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        IOptions<VitalRangeOptions> ranges,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(Perm.Clinical.EncountersView, cancellationToken);

        // Only through the account's own active link that allows records. Staff scopes don't apply here: staff read
        // encounters through the staff endpoints. Anything else is a 404, whether or not the patient exists.
        Guid? linkId = null;
        if (scope.Includes(PermissionScope.Linked) && scope.UserId is { } userId)
        {
            linkId = await db.Set<PatientAccountLink>().AsNoTracking()
                .Where(l => l.UserId == userId && l.PatientId == patientId && l.Status == LinkStatus.Active && l.CanViewRecords)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var target = new AuditTarget(nameof(Patient), patientId);
        if (linkId is null)
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Clinical.EncountersView, target, cancellationToken);
            return ClinicalErrors.PortalRecordsNotFound.ToProblemResult();
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Clinical.EncounterViewed,
            new AuditOptions
            {
                Target = target,
                PatientId = patientId,
                ActingForPatientLinkId = linkId,
                Details = new { via = "portal" },
            },
            cancellationToken);

        var visits = await VisitsAsync(db, patientId, cancellationToken);
        var vitals = await VitalsAsync(db, patientId, ranges.Value, cancellationToken);
        var appointments = await AppointmentsAsync(db, patientId, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new PortalRecordsResponse(patientId, visits, vitals, appointments));
    }

    private static async Task<List<PortalVisitResponse>> VisitsAsync(AppDbContext db, Guid patientId, CancellationToken cancellationToken)
    {
        var rows = await (
            from e in db.Set<Encounter>().AsNoTracking()
            join d in db.Set<Employee>() on e.AttendingPhysicianId equals d.Id
            join sp in db.Set<Specialization>() on d.Physician!.SpecializationId equals sp.Id
            where e.PatientId == patientId && e.Status != EncounterStatus.Voided
            orderby e.StartedAt descending, e.Id descending
            select new
            {
                e.Id,
                e.Type,
                e.Status,
                e.StartedAt,
                e.EndedAt,
                e.ChiefComplaint,
                e.DiagnosisSummary,
                Physician = new PortalPhysicianRef(d.FirstName, d.LastName, sp.Name),
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var disposition = await db.Set<Admission>().AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.DischargedAt != null)
            .ToDictionaryAsync(a => a.Id, a => a.DischargeDisposition, cancellationToken);

        // Only the discharge summary note ever leaves: the latest one that hasn't been voided.
        var summaries = (await db.Set<ClinicalNote>().AsNoTracking()
                .Where(n => ids.Contains(n.EncounterId) && n.NoteType == NoteType.DischargeSummary && n.VoidedAt == null)
                .Select(n => new { n.EncounterId, n.Body, n.AuthoredAt })
                .ToListAsync(cancellationToken))
            .GroupBy(n => n.EncounterId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.AuthoredAt).First().Body);

        return
        [
            .. rows.Select(r => new PortalVisitResponse(
                r.Id,
                r.Type,
                r.Status,
                r.StartedAt,
                r.EndedAt,
                r.ChiefComplaint,
                r.DiagnosisSummary,
                summaries.GetValueOrDefault(r.Id),
                disposition.GetValueOrDefault(r.Id),
                r.Physician)),
        ];
    }

    private static async Task<List<PortalVitalsResponse>> VitalsAsync(
        AppDbContext db,
        Guid patientId,
        VitalRangeOptions ranges,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<VitalSigns>().AsNoTracking()
            .Where(v => v.PatientId == patientId && v.VoidedAt == null)
            .OrderByDescending(v => v.RecordedAt)
            .ThenByDescending(v => v.Id)
            .Take(MaxVitals)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(v => new PortalVitalsResponse(
                v.Id,
                v.RecordedAt,
                v.TemperatureC,
                v.SystolicBp,
                v.DiastolicBp,
                v.HeartRate,
                v.RespiratoryRate,
                v.OxygenSaturation,
                v.PainScore,
                v.WeightKg,
                v.HeightCm,
                v.Bmi,
                VitalEndpoints.Flags(v, ranges))),
        ];
    }

    /// <summary>Upcoming appointments soonest first, then recent ones, newest first.</summary>
    private static async Task<List<PortalAppointmentResponse>> AppointmentsAsync(
        AppDbContext db,
        Guid patientId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = (now - AppointmentHistory).UtcDateTime;
        var rows = await (
            from a in db.Set<Appointment>().AsNoTracking()
            join s in db.Set<AvailabilitySlot>() on a.SlotId equals s.Id
            join d in db.Set<Employee>() on s.PhysicianId equals d.Id
            join sp in db.Set<Specialization>() on d.Physician!.SpecializationId equals sp.Id
            where a.PatientId == patientId && s.During.UpperBound > since
            select new { a.Id, a.Status, a.Reason, Slot = s, Physician = new PortalPhysicianRef(d.FirstName, d.LastName, sp.Name) })
            .ToListAsync(cancellationToken);

        var responses = rows
            .Select(r => new PortalAppointmentResponse(r.Id, r.Status, r.Reason, r.Slot.StartsAt, r.Slot.EndsAt, r.Physician))
            .ToList();
        return
        [
            .. responses.Where(a => a.EndsAt > now).OrderBy(a => a.StartsAt),
            .. responses.Where(a => a.EndsAt <= now).OrderByDescending(a => a.StartsAt),
        ];
    }
}

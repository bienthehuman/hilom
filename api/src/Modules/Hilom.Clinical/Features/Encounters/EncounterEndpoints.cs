using Hilom.Access.Domain;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Scopes;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.Scheduling.Domain;
using Hilom.Scheduling.Visits;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Features.Encounters;

/// <summary>
/// Encounters (docs/DATA_MODEL.md, clinical): check-in opens a clinic visit; lists and the encounter page
/// (<c>clinical.encounters.view</c>, opening one is a sensitive read); the physician updates the summary and
/// closes it (<c>clinical.encounters.manage</c>), which completes the appointment.
/// </summary>
internal static class EncounterEndpoints
{
    /// <summary>
    /// The front desk checks a patient in for a confirmed appointment on its day. In one transaction: the
    /// appointment becomes checked in, and an outpatient encounter opens with the appointment's physician
    /// as attending and the booking reason as chief complaint.
    /// </summary>
    public static async Task<Results<Created<CheckInResponse>, ProblemHttpResult>> CheckIn(
        CheckInRequest request,
        AppDbContext db,
        IAppointmentVisits visits,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var checkedIn = await visits.CheckInAsync(request.AppointmentId, request.Version, cancellationToken);
        if (!checkedIn.IsSuccess)
        {
            return checkedIn.Error.ToProblemResult();
        }

        var visit = checkedIn.Value;
        var encounter = Encounter.CheckIn(visit.PatientId, visit.AppointmentId, visit.PhysicianId, visit.Reason, time.GetUtcNow());
        db.Add(encounter);
        auditLog.Record(
            AuditEvents.Clinical.EncounterOpened,
            new AuditOptions
            {
                Target = Target(encounter),
                PatientId = encounter.PatientId,
                Details = new { type = encounter.Type, appointmentId = visit.AppointmentId, attendingPhysicianId = visit.PhysicianId },
            });

        // A second check-in of the same appointment fails on the appointment's version or the unique index (409).
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/clinical/encounters/{encounter.Id}", new CheckInResponse(encounter.Id, visit.AppointmentId));
    }

    /// <summary>Encounters in the caller's reach, newest first; optionally one patient's, or by status.</summary>
    public static async Task<Ok<PagedResponse<EncounterSummaryResponse>>> ListEncounters(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        Guid? patientId,
        EncounterStatus? status,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var reach = await EncounterReach.ResolveAsync(permissions, staff, Perm.Clinical.EncountersView, cancellationToken);
        var query = reach.Filter(db.Set<Encounter>().AsNoTracking())
            .Where(e => (patientId == null || e.PatientId == patientId) && (status == null || e.Status == status));

        var total = await query.CountAsync(cancellationToken);
        var items = await (
            from e in query
            join p in db.Set<Patient>() on e.PatientId equals p.Id
            join d in db.Set<Employee>() on e.AttendingPhysicianId equals d.Id
            join sp in db.Set<Specialization>() on d.Physician!.SpecializationId equals sp.Id
            orderby e.StartedAt descending, e.Id descending
            select new EncounterSummaryResponse(
                e.Id,
                e.Type,
                e.Status,
                e.StartedAt,
                e.EndedAt,
                e.ChiefComplaint,
                new EncounterPatientRef(p.Id, p.Mrn, p.FirstName, p.MiddleName, p.LastName, p.BirthDate, p.Sex),
                new EncounterPhysicianRef(d.Id, d.FirstName, d.LastName, sp.Name)))
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<EncounterSummaryResponse>(items, total, pageNumber, size));
    }

    /// <summary>Opening an encounter is a sensitive read: logged once per user per patient per 15 minutes.</summary>
    public static async Task<Results<Ok<EncounterResponse>, ProblemHttpResult>> GetEncounter(
        Guid id,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var encounter = await FindInScopeAsync(db, permissions, staff, auditLog, Perm.Clinical.EncountersView, id, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Clinical.EncounterViewed,
            new AuditOptions { Target = Target(encounter), PatientId = encounter.PatientId },
            cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, permissions, staff, encounter, cancellationToken));
    }

    /// <summary>The attending physician updates the chief complaint and diagnosis summary while the encounter is open.</summary>
    public static async Task<Results<Ok<EncounterResponse>, ProblemHttpResult>> UpdateEncounter(
        Guid id,
        UpdateEncounterRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var encounter = await FindInScopeAsync(db, permissions, staff, auditLog, Perm.Clinical.EncountersManage, id, track: true, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        var changed = encounter.UpdateSummary(request.ChiefComplaint, request.DiagnosisSummary);
        if (!changed.IsSuccess)
        {
            return changed.Error.ToProblemResult();
        }

        if (changed.Value.Count == 0)
        {
            auditLog.NothingToRecord();
        }
        else
        {
            // Which fields changed, never the clinical text itself (docs/AUDIT.md, rule 7).
            auditLog.Record(
                AuditEvents.Clinical.EncounterChanged,
                new AuditOptions { Target = Target(encounter), PatientId = encounter.PatientId, Details = new { fields = changed.Value } });
        }

        // Always write the row, so a stale version is caught even when nothing changed.
        SaveWithVersion(db, encounter, request.Version);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ToResponseAsync(db, permissions, staff, encounter, cancellationToken));
    }

    /// <summary>Ends the visit. A clinic visit's appointment becomes completed in the same transaction.</summary>
    public static async Task<Results<Ok<EncounterResponse>, ProblemHttpResult>> CloseEncounter(
        Guid id,
        CloseEncounterRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAppointmentVisits visits,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await FindInScopeAsync(db, permissions, staff, auditLog, Perm.Clinical.EncountersManage, id, track: true, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        var closed = encounter.Close(time.GetUtcNow());
        if (!closed.IsSuccess)
        {
            return closed.Error.ToProblemResult();
        }

        if (encounter.AppointmentId is { } appointmentId)
        {
            var completed = await visits.CompleteAsync(appointmentId, cancellationToken);
            if (!completed.IsSuccess)
            {
                return completed.Error.ToProblemResult();
            }
        }

        auditLog.Record(
            AuditEvents.Clinical.EncounterClosed,
            new AuditOptions
            {
                Target = Target(encounter),
                PatientId = encounter.PatientId,
                Details = new { type = encounter.Type, appointmentId = encounter.AppointmentId },
            });
        SaveWithVersion(db, encounter, request.Version);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ToResponseAsync(db, permissions, staff, encounter, cancellationToken));
    }

    /// <summary>
    /// The encounter, if it exists and <paramref name="permission"/>'s scope reaches it. One outside the scope is
    /// reported as not found, and the attempt is logged as <c>access.denied</c>.
    /// </summary>
    internal static async Task<Encounter?> FindInScopeAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        Permission permission,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        var encounters = track ? db.Set<Encounter>() : db.Set<Encounter>().AsNoTracking();
        var encounter = await encounters.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (encounter is null)
        {
            return null;
        }

        var reach = await EncounterReach.ResolveAsync(permissions, staff, permission, cancellationToken);
        if (await reach.Filter(db.Set<Encounter>().Where(e => e.Id == id)).AnyAsync(cancellationToken))
        {
            return encounter;
        }

        await auditLog.RecordOutOfScopeAsync(permission, Target(encounter), cancellationToken);
        return null;
    }

    private static void SaveWithVersion(AppDbContext db, Encounter encounter, uint version)
    {
        var entry = db.Entry(encounter);
        entry.Property(e => e.Version).OriginalValue = version;
        entry.Property(e => e.UpdatedAt).IsModified = true;
    }

    private static async Task<EncounterResponse> ToResponseAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        Encounter e,
        CancellationToken cancellationToken)
    {
        var patient = await db.Set<Patient>().AsNoTracking()
            .Where(p => p.Id == e.PatientId)
            .Select(p => new EncounterPatientRef(p.Id, p.Mrn, p.FirstName, p.MiddleName, p.LastName, p.BirthDate, p.Sex))
            .SingleAsync(cancellationToken);
        var physician = await (
            from d in db.Set<Employee>()
            join sp in db.Set<Specialization>() on d.Physician!.SpecializationId equals sp.Id
            where d.Id == e.AttendingPhysicianId
            select new EncounterPhysicianRef(d.Id, d.FirstName, d.LastName, sp.Name))
            .SingleAsync(cancellationToken);
        DateTimeOffset? appointmentStartsAt = null;
        if (e.AppointmentId is { } appointmentId)
        {
            var during = await (
                from a in db.Set<Appointment>()
                join s in db.Set<AvailabilitySlot>() on a.SlotId equals s.Id
                where a.Id == appointmentId
                select s.During)
                .SingleAsync(cancellationToken);
            appointmentStartsAt = new DateTimeOffset(during.LowerBound, TimeSpan.Zero);
        }

        var openedBy = e.CreatedBy is { } userId
            ? await db.Set<User>().Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var allergies = (await db.Set<PatientAllergy>().AsNoTracking()
                .Where(a => a.PatientId == e.PatientId && a.Status == AllergyStatus.Active)
                .Select(a => new EncounterAllergyRef(a.Allergen, a.Severity, a.Reaction))
                .ToListAsync(cancellationToken))
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Allergen, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var manage = await EncounterReach.ResolveAsync(permissions, staff, Perm.Clinical.EncountersManage, cancellationToken);
        var canManage = e.Status == EncounterStatus.Open
            && await manage.Filter(db.Set<Encounter>().Where(x => x.Id == e.Id)).AnyAsync(cancellationToken);
        var canRecord = e.Status == EncounterStatus.Open && await staff.MyEmployeeIdAsync(cancellationToken) is not null;
        var record = await EncounterReach.ResolveAsync(permissions, staff, Perm.Clinical.VitalsRecord, cancellationToken);
        var canRecordVitals = canRecord && await record.Filter(db.Set<Encounter>().Where(x => x.Id == e.Id)).AnyAsync(cancellationToken);
        var write = await EncounterReach.ResolveAsync(permissions, staff, Perm.Clinical.NotesWrite, cancellationToken);
        var canWriteNotes = canRecord && await write.Filter(db.Set<Encounter>().Where(x => x.Id == e.Id)).AnyAsync(cancellationToken);

        return new EncounterResponse(
            e.Id,
            e.Type,
            e.Status,
            e.StartedAt,
            e.EndedAt,
            e.ChiefComplaint,
            e.DiagnosisSummary,
            patient,
            physician,
            e.AppointmentId,
            appointmentStartsAt,
            e.ParentEncounterId,
            openedBy,
            allergies,
            canManage,
            canRecordVitals,
            canWriteNotes,
            e.Version);
    }

    internal static AuditTarget Target(Encounter encounter) => new(nameof(Encounter), encounter.Id);
}

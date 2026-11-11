using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Features.Notes;
using Hilom.Clinical.Persistence;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Features.Admissions;

/// <summary>
/// Discharge (docs/DATA_MODEL.md, admissions): end a ward stay with a disposition and a discharge summary
/// (<c>clinical.admissions.discharge</c>: all, or own = stays I attend). In one transaction the patient leaves
/// their bed, the admission records the discharge, the inpatient encounter closes, and the summary is saved as
/// a <c>discharge_summary</c> clinical note, so it is edited, corrected and voided like any other note.
/// </summary>
internal static class DischargeEndpoints
{
    public static async Task<Results<Ok<EncounterResponse>, ProblemHttpResult>> Discharge(
        Guid id,
        DischargeRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.AdmissionsDischarge, id, track: true, cancellationToken);
        if (encounter is null || encounter.Type != EncounterType.Inpatient)
        {
            return ClinicalErrors.AdmissionNotFound.ToProblemResult();
        }

        // Lock the patient, then read and check: a racing transfer or discharge waits here and then sees it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await StayLocks.LockAsync(db, [encounter.PatientId], cancellationToken);
        var admission = await db.Set<Admission>().SingleAsync(a => a.Id == id, cancellationToken);

        if (admission.IsDischarged)
        {
            return ClinicalErrors.AlreadyDischarged.ToProblemResult();
        }

        // The summary is a note, and notes name the employee who wrote them.
        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return ClinicalErrors.NoEmployeeRecord.ToProblemResult();
        }

        var current = await db.Set<BedAssignment>()
            .SingleAsync(b => b.AdmissionId == id && b.During.UpperBoundInfinite, cancellationToken);
        var lastBed = await (
            from b in db.Set<Bed>()
            join w in db.Set<Ward>() on b.WardId equals w.Id
            where b.Id == current.BedId
            select new { b.Label, w.Code })
            .SingleAsync(cancellationToken);

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Discharging needs a signed-in user.");
        var discharged = admission.Discharge(encounter, current, request.Disposition, request.Summary, userId, employeeId, time.GetUtcNow());
        if (!discharged.IsSuccess)
        {
            return discharged.Error.ToProblemResult();
        }

        var summary = discharged.Value;
        db.Add(summary);
        auditLog.Record(
            AuditEvents.Clinical.PatientDischarged,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Admission), admission.Id),
                PatientId = admission.PatientId,
                Details = new { disposition = request.Disposition, ward = lastBed.Code, bed = lastBed.Label, summaryNoteId = summary.Id },
            });
        NoteEndpoints.RecordWritten(auditLog, summary);
        auditLog.Record(
            AuditEvents.Clinical.EncounterClosed,
            new AuditOptions
            {
                Target = EncounterEndpoints.Target(encounter),
                PatientId = encounter.PatientId,
                Details = new { type = encounter.Type, appointmentId = encounter.AppointmentId },
            });

        // The stay's version guards against acting on a stale page; the admission row changes, so EF checks it.
        db.Entry(admission).Property(a => a.Version).OriginalValue = request.Version;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(await EncounterEndpoints.ToResponseAsync(db, permissions, staff, encounter, cancellationToken));
    }
}

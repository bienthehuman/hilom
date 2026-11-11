using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Persistence;
using Hilom.Clinical.Scopes;
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
using Npgsql;

namespace Hilom.Clinical.Features.Admissions;

/// <summary>
/// Transfers (docs/DATA_MODEL.md, bed_assignments): move an admitted patient to another free bed, on any ward,
/// with a reason (<c>clinical.admissions.transfer</c>: all; ward = patients in a bed on my wards today; own = stays
/// I attend). The current bed assignment is closed and the next one opened in one transaction, so ward access
/// moves with the patient on the next request.
/// </summary>
internal static class TransferEndpoints
{
    /// <summary>Free beds to move this stay's patient to. 404 when the stay is outside the caller's transfer scope.</summary>
    public static async Task<Results<Ok<List<FreeBedWardResponse>>, ProblemHttpResult>> ListTransferBeds(
        Guid id,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.AdmissionsTransfer, id, track: false, cancellationToken);
        if (encounter is null || encounter.Type != EncounterType.Inpatient)
        {
            return ClinicalErrors.AdmissionNotFound.ToProblemResult();
        }

        return TypedResults.Ok(await AdmissionEndpoints.FreeBedsAsync(db, wardId: null, cancellationToken));
    }

    public static async Task<Results<Ok<TransferResponse>, ProblemHttpResult>> Transfer(
        Guid id,
        TransferRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.AdmissionsTransfer, id, track: false, cancellationToken);
        if (encounter is null || encounter.Type != EncounterType.Inpatient)
        {
            return ClinicalErrors.AdmissionNotFound.ToProblemResult();
        }

        // Lock the patient and the target bed, then read and check: a racing admission, transfer or discharge
        // waits here and then sees what the other one saved.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await StayLocks.LockAsync(db, [encounter.PatientId, request.BedId], cancellationToken);
        var admission = await db.Set<Admission>().SingleAsync(a => a.Id == id, cancellationToken);

        if (admission.IsDischarged)
        {
            return ClinicalErrors.AlreadyDischarged.ToProblemResult();
        }

        var current = await db.Set<BedAssignment>()
            .SingleAsync(b => b.AdmissionId == id && b.During.UpperBoundInfinite, cancellationToken);
        var from = await BedAsync(db, current.BedId, activeOnly: false, cancellationToken);
        var to = await BedAsync(db, request.BedId, activeOnly: true, cancellationToken);
        if (to is null)
        {
            return ClinicalErrors.BedNotAvailable.ToProblemResult();
        }

        // Friendly answer; the exclusion constraint below stays as the final guard.
        if (to.BedId != current.BedId
            && await db.Set<BedAssignment>().AnyAsync(a => a.BedId == to.BedId && a.During.UpperBoundInfinite, cancellationToken))
        {
            return ClinicalErrors.BedTaken.ToProblemResult();
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Transferring needs a signed-in user.");
        var moved = admission.Transfer(current, to.BedId, to.WardId, request.Reason, userId, time.GetUtcNow());
        if (!moved.IsSuccess)
        {
            return moved.Error.ToProblemResult();
        }

        db.Add(moved.Value);
        auditLog.Record(
            AuditEvents.Clinical.PatientTransferred,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Admission), admission.Id),
                PatientId = admission.PatientId,
                Details = new
                {
                    fromWard = from!.WardCode,
                    fromBed = from.Label,
                    toWard = to.WardCode,
                    toBed = to.Label,
                    reason = moved.Value.Reason,
                },
            });

        // The stay's version guards against acting on a stale page: a transfer or discharge meanwhile is a 409.
        var entry = db.Entry(admission);
        entry.Property(a => a.Version).OriginalValue = request.Version;
        entry.Property(a => a.UpdatedAt).IsModified = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: BedAssignmentConfiguration.OnePatientPerBed,
        })
        {
            return ClinicalErrors.BedTaken.ToProblemResult();
        }

        // Access follows the bed: a ward nurse who moved the patient off their wards can't open the stay now.
        var view = await EncounterReach.ResolveAsync(db, permissions, staff, Perm.Clinical.EncountersView, cancellationToken);
        var canStillView = await view.Filter(db.Set<Encounter>().Where(e => e.Id == id)).AnyAsync(cancellationToken);
        return TypedResults.Ok(new TransferResponse(
            id,
            new AdmissionWardRef(to.WardId, to.WardCode, to.WardName),
            new AdmissionBedRef(to.BedId, to.Label),
            canStillView));
    }

    private sealed record BedInfo(Guid BedId, string Label, Guid WardId, string WardCode, string WardName);

    private static Task<BedInfo?> BedAsync(AppDbContext db, Guid bedId, bool activeOnly, CancellationToken cancellationToken) =>
        (from b in db.Set<Bed>().AsNoTracking()
         join w in db.Set<Ward>() on b.WardId equals w.Id
         where b.Id == bedId && (!activeOnly || (b.IsActive && w.IsActive))
         select new BedInfo(b.Id, b.Label, w.Id, w.Code, w.Name))
            .SingleOrDefaultAsync(cancellationToken);
}

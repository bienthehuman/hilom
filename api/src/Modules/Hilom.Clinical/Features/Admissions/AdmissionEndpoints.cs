using System.Globalization;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Persistence;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Clinical.Features.Admissions;

/// <summary>
/// Admission (docs/DATA_MODEL.md, clinical): admit a patient, from a clinic visit or directly, into a free bed
/// (<c>clinical.admissions.admit</c>). The inpatient encounter, its admission and the first bed assignment are
/// saved in one transaction, and the database keeps one admission per patient and one patient per bed.
/// </summary>
internal static class AdmissionEndpoints
{
    /// <summary>"W2" before "W10", "Bed 9" before "Bed 10".</summary>
    private static readonly StringComparer Natural =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

    public static async Task<Results<Created<AdmitResponse>, ProblemHttpResult>> Admit(
        AdmitRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == request.PatientId, cancellationToken))
        {
            return ClinicalErrors.PatientNotFound.ToProblemResult();
        }

        if (request.ParentEncounterId is { } parentId
            && !await db.Set<Encounter>().AnyAsync(
                e => e.Id == parentId
                    && e.PatientId == request.PatientId
                    && e.Type == EncounterType.Outpatient
                    && e.Status != EncounterStatus.Voided,
                cancellationToken))
        {
            return ClinicalErrors.InvalidParentEncounter.ToProblemResult();
        }

        var physician = await db.Set<Employee>().AsNoTracking()
            .Where(e => e.Id == request.AttendingPhysicianId && e.Physician != null && e.Status != EmployeeStatus.Separated)
            .Select(e => new { e.Id, e.EmployeeNo })
            .SingleOrDefaultAsync(cancellationToken);
        if (physician is null)
        {
            return ClinicalErrors.PhysicianNotAvailable.ToProblemResult();
        }

        var bed = await (
            from b in db.Set<Bed>()
            join w in db.Set<Ward>() on b.WardId equals w.Id
            where b.Id == request.BedId && b.IsActive && w.IsActive
            select new { BedId = b.Id, b.Label, WardId = w.Id, w.Code })
            .SingleOrDefaultAsync(cancellationToken);
        if (bed is null)
        {
            return ClinicalErrors.BedNotAvailable.ToProblemResult();
        }

        // Friendly answers for the usual case; the exclusion constraints below catch the races.
        if (await db.Set<Admission>().AnyAsync(a => a.PatientId == request.PatientId && a.DischargedAt == null, cancellationToken))
        {
            return ClinicalErrors.AlreadyAdmitted.ToProblemResult();
        }

        if (await db.Set<BedAssignment>().AnyAsync(a => a.BedId == bed.BedId && a.During.UpperBoundInfinite, cancellationToken))
        {
            return ClinicalErrors.BedTaken.ToProblemResult();
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Admitting needs a signed-in user.");
        var (encounter, admission, assignment) = Admission.Admit(
            new AdmitPatient(
                request.PatientId,
                request.ParentEncounterId,
                physician.Id,
                bed.BedId,
                request.AdmittingDiagnosis,
                request.Reason),
            bed.WardId,
            userId,
            time.GetUtcNow());
        db.AddRange(encounter, admission, assignment);

        auditLog.Record(
            AuditEvents.Clinical.EncounterOpened,
            new AuditOptions
            {
                Target = EncounterEndpoints.Target(encounter),
                PatientId = encounter.PatientId,
                Details = new { type = encounter.Type, parentEncounterId = encounter.ParentEncounterId, attendingPhysicianId = physician.Id },
            });
        auditLog.Record(
            AuditEvents.Clinical.PatientAdmitted,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Admission), admission.Id),
                PatientId = admission.PatientId,
                Details = new { ward = bed.Code, bed = bed.Label, attending = physician.EmployeeNo },
            });

        // One SaveChanges is one transaction: the encounter, admission and bed are all saved, or none of them.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
        } postgres)
        {
            return (postgres.ConstraintName == AdmissionConfiguration.OneAtATime
                ? ClinicalErrors.AlreadyAdmitted
                : ClinicalErrors.BedTaken).ToProblemResult();
        }

        return TypedResults.Created(
            $"/api/clinical/encounters/{encounter.Id}",
            new AdmitResponse(encounter.Id, bed.WardId, bed.BedId));
    }

    /// <summary>Active beds in active wards with nobody in them now, grouped by ward, in natural order.</summary>
    public static async Task<Ok<List<FreeBedWardResponse>>> ListFreeBeds(
        AppDbContext db,
        Guid? wardId,
        CancellationToken cancellationToken)
    {
        var beds = await (
            from b in db.Set<Bed>().AsNoTracking()
            join w in db.Set<Ward>() on b.WardId equals w.Id
            where b.IsActive && w.IsActive
                && (wardId == null || w.Id == wardId)
                && !db.Set<BedAssignment>().Any(a => a.BedId == b.Id && a.During.UpperBoundInfinite)
            select new { Ward = w, BedId = b.Id, b.Label })
            .ToListAsync(cancellationToken);

        // A hospital has tens of wards and hundreds of beds: group and sort naturally in memory.
        var wards = beds
            .GroupBy(b => b.Ward.Id)
            .Select(g =>
            {
                var w = g.First().Ward;
                return new FreeBedWardResponse(
                    w.Id,
                    w.Code,
                    w.Name,
                    w.WardType,
                    [.. g.OrderBy(b => b.Label, Natural).Select(b => new FreeBedResponse(b.BedId, b.Label))]);
            })
            .OrderBy(w => w.Code, Natural)
            .ToList();
        return TypedResults.Ok(wards);
    }

    /// <summary>Current physicians on staff, for choosing the attending physician.</summary>
    public static async Task<Ok<List<AttendingPhysicianResponse>>> ListAttendingPhysicians(
        AppDbContext db,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await (
            from e in db.Set<Employee>().AsNoTracking()
            join s in db.Set<Specialization>() on e.Physician!.SpecializationId equals s.Id
            where e.Physician != null && e.Status != EmployeeStatus.Separated
            orderby e.LastName, e.FirstName
            select new AttendingPhysicianResponse(e.Id, e.FirstName, e.LastName, s.Name))
            .ToListAsync(cancellationToken));
}

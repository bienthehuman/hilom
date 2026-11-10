using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.Patients.Features.Registry;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Patients.Features.Allergies;

/// <summary>A patient's allergies: list, record, edit, resolve or mark entered in error. Nothing is deleted.</summary>
internal static class AllergyEndpoints
{
    /// <summary>
    /// Active entries first, most severe first; then history, newest first. Part of the patient's record, so it
    /// is logged as a record view like opening the patient.
    /// </summary>
    public static async Task<Results<Ok<List<AllergyResponse>>, ProblemHttpResult>> ListAllergies(
        Guid patientId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var patient = await PatientEndpoints.FindInScopeAsync(db, permissions, auditLog, Perm.Patients.View, patientId, track: false, cancellationToken);
        if (patient is null)
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Patients.RecordViewed,
            new AuditOptions { Target = PatientEndpoints.Target(patient), PatientId = patient.Id },
            cancellationToken);

        var allergies = await db.Set<PatientAllergy>().AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .ToListAsync(cancellationToken);
        var ordered = allergies
            .OrderBy(a => a.Status == AllergyStatus.Active ? 0 : 1)
            .ThenByDescending(a => a.Status == AllergyStatus.Active ? (int)a.Severity : 0)
            .ThenByDescending(a => a.Status == AllergyStatus.Active ? DateTimeOffset.MinValue : a.UpdatedAt)
            .ThenBy(a => a.Allergen, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return TypedResults.Ok(await ToResponsesAsync(db, ordered, cancellationToken));
    }

    public static async Task<Results<Created<AllergyResponse>, ProblemHttpResult>> RecordAllergy(
        Guid patientId,
        RecordAllergyRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var patient = await FindPatientToManageAsync(db, permissions, auditLog, patientId, cancellationToken);
        if (patient is null)
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        var allergy = new PatientAllergy(patientId, request.ToDetails());
        if (await ActiveDuplicateExistsAsync(db, allergy, cancellationToken))
        {
            return PatientErrors.AllergyAlreadyRecorded.ToProblemResult();
        }

        db.Add(allergy);
        auditLog.Record(
            AuditEvents.Patients.AllergyRecorded,
            new AuditOptions
            {
                Target = Target(allergy),
                PatientId = patientId,
                Details = new
                {
                    allergen = allergy.Allergen,
                    category = EnumText.ToText(allergy.Category),
                    severity = EnumText.ToText(allergy.Severity),
                },
            });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/patients/{patientId}/allergies/{allergy.Id}",
            (await ToResponsesAsync(db, [allergy], cancellationToken))[0]);
    }

    public static async Task<Results<Ok<AllergyResponse>, ProblemHttpResult>> UpdateAllergy(
        Guid patientId,
        Guid id,
        UpdateAllergyRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var allergy = await FindAllergyToManageAsync(db, permissions, auditLog, patientId, id, cancellationToken);
        if (!allergy.IsSuccess)
        {
            return allergy.Error.ToProblemResult();
        }

        var updated = allergy.Value.Update(request.ToDetails());
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblemResult();
        }

        if (updated.Value.Contains("allergen") && await ActiveDuplicateExistsAsync(db, allergy.Value, cancellationToken))
        {
            return PatientErrors.AllergyAlreadyRecorded.ToProblemResult();
        }

        if (updated.Value.Count == 0)
        {
            auditLog.NothingToRecord();
        }
        else
        {
            auditLog.Record(
                AuditEvents.Patients.AllergyChanged,
                new AuditOptions
                {
                    Target = Target(allergy.Value),
                    PatientId = patientId,
                    Details = new { allergen = allergy.Value.Allergen, change = "edited", fields = updated.Value },
                });
        }

        await SaveWithVersionAsync(db, allergy.Value, request.Version, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [allergy.Value], cancellationToken))[0]);
    }

    public static async Task<Results<Ok<AllergyResponse>, ProblemHttpResult, ValidationProblem>> ChangeAllergyStatus(
        Guid patientId,
        Guid id,
        ChangeAllergyStatusRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var allergy = await FindAllergyToManageAsync(db, permissions, auditLog, patientId, id, cancellationToken);
        if (!allergy.IsSuccess)
        {
            return allergy.Error.ToProblemResult();
        }

        var changed = allergy.Value.ChangeStatus(request.Status!.Value, request.Note);
        if (!changed.IsSuccess)
        {
            return changed.Error.Type == ErrorType.Validation
                ? changed.Error.ToValidationResult()
                : changed.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Patients.AllergyChanged,
            new AuditOptions
            {
                Target = Target(allergy.Value),
                PatientId = patientId,
                Details = new
                {
                    allergen = allergy.Value.Allergen,
                    change = EnumText.ToText(allergy.Value.Status),
                    note = allergy.Value.StatusNote,
                },
            });
        await SaveWithVersionAsync(db, allergy.Value, request.Version, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [allergy.Value], cancellationToken))[0]);
    }

    private static Task<Patient?> FindPatientToManageAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        Guid patientId,
        CancellationToken cancellationToken) =>
        PatientEndpoints.FindInScopeAsync(db, permissions, auditLog, Perm.Patients.AllergiesManage, patientId, track: false, cancellationToken);

    /// <summary>The allergy, tracked, if its patient is in the caller's <c>allergies.manage</c> scope.</summary>
    private static async Task<Result<PatientAllergy>> FindAllergyToManageAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        Guid patientId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var patient = await FindPatientToManageAsync(db, permissions, auditLog, patientId, cancellationToken);
        if (patient is null)
        {
            return Result.Failure<PatientAllergy>(PatientErrors.PatientNotFound);
        }

        var allergy = await db.Set<PatientAllergy>().SingleOrDefaultAsync(a => a.Id == id && a.PatientId == patientId, cancellationToken);
        return allergy is null
            ? Result.Failure<PatientAllergy>(PatientErrors.AllergyNotFound)
            : Result.Success(allergy);
    }

    private static Task<bool> ActiveDuplicateExistsAsync(AppDbContext db, PatientAllergy allergy, CancellationToken cancellationToken) =>
        db.Set<PatientAllergy>().AnyAsync(
            a => a.PatientId == allergy.PatientId
                && a.Allergen == allergy.Allergen
                && a.Status == AllergyStatus.Active
                && a.Id != allergy.Id,
            cancellationToken);

    /// <summary>Always writes the row, so a stale version is caught even when nothing changed.</summary>
    private static Task<int> SaveWithVersionAsync(AppDbContext db, PatientAllergy allergy, uint version, CancellationToken cancellationToken)
    {
        var entry = db.Entry(allergy);
        entry.Property(a => a.Version).OriginalValue = version;
        entry.Property(a => a.UpdatedAt).IsModified = true;
        return db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<AllergyResponse>> ToResponsesAsync(
        AppDbContext db,
        IReadOnlyList<PatientAllergy> allergies,
        CancellationToken cancellationToken)
    {
        var userIds = allergies.Where(a => a.CreatedBy is not null).Select(a => a.CreatedBy!.Value).Distinct().ToList();
        var names = await db.Set<User>()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return
        [
            .. allergies.Select(a => new AllergyResponse(
                a.Id,
                a.Allergen,
                a.Category,
                a.Severity,
                a.Reaction,
                a.Status,
                a.StatusNote,
                a.CreatedAt,
                a.CreatedBy is { } by ? names.GetValueOrDefault(by) : null,
                a.UpdatedAt,
                a.Version)),
        ];
    }

    private static AuditTarget Target(PatientAllergy allergy) => new(nameof(PatientAllergy), allergy.Id);
}

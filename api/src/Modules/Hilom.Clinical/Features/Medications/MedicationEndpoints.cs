using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Features.Voiding;
using Hilom.Clinical.Scopes;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Features.Medications;

/// <summary>
/// The medication administration record (docs/DATA_MODEL.md, medication_administrations): each medication order
/// on the encounter with the doses recorded against it (<c>clinical.encounters.view</c>), recording a dose against
/// an active order (<c>clinical.medications.administer</c>: all, or ward = the patient is in a bed on my ward
/// today) and voiding one entered in error (<c>clinical.records.void</c>). A dose not given needs a reason, and a
/// patient with active allergies needs the nurse to confirm they checked them.
/// </summary>
internal static class MedicationEndpoints
{
    /// <summary>Clock skew allowed between the nurse's device and the server.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The encounter's medication orders, active ones first, each with its doses newest first (voided ones
    /// included). Logged as a view of the encounter.
    /// </summary>
    public static async Task<Results<Ok<List<MarOrderResponse>>, ProblemHttpResult>> ListMar(
        Guid encounterId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.EncountersView, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Clinical.EncounterViewed,
            new AuditOptions { Target = EncounterEndpoints.Target(encounter), PatientId = encounter.PatientId },
            cancellationToken);

        var orders = await (
            from o in db.Set<PhysicianOrder>().AsNoTracking()
            join e in db.Set<Employee>() on o.OrderedBy equals e.Id
            where o.EncounterId == encounterId && o.OrderType == OrderType.Medication
            orderby o.OrderedAt descending, o.Id descending
            select new { Order = o, Name = e.FirstName + " " + e.LastName })
            .ToListAsync(cancellationToken);
        var allergens = await ActiveAllergensAsync(db, encounter.PatientId, cancellationToken);
        var canRecord = await CanAdministerAsync(db, permissions, staff, encounter, cancellationToken);
        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        var doses = (await ToResponsesAsync(db, db.Set<MedicationAdministration>().Where(a => a.EncounterId == encounterId), reach, cancellationToken))
            .ToLookup(a => a.OrderId);

        return TypedResults.Ok(orders
            .OrderBy(r => r.Order.IsActive ? 0 : 1)
            .Select(r =>
            {
                var o = r.Order;
                return new MarOrderResponse(
                    o.Id,
                    o.MedicationName ?? "",
                    o.Dose ?? "",
                    o.Route ?? "",
                    o.Frequency ?? "",
                    o.Details,
                    o.Status,
                    o.OrderedAt,
                    r.Name,
                    AllergyCheck.Matches(o.MedicationName, allergens),
                    canRecord && o.IsActive,
                    [.. doses[o.Id]]);
            })
            .ToList());
    }

    /// <summary>
    /// Records a dose against an active medication order on an open encounter: given (with the dose) or held,
    /// refused or not available (with the reason). Recorded by the signed-in employee, now or earlier.
    /// </summary>
    public static async Task<Results<Created<MedicationAdministrationResponse>, ProblemHttpResult>> RecordAdministration(
        Guid encounterId,
        RecordAdministrationRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.MedicationsAdminister, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        if (encounter.Status != EncounterStatus.Open)
        {
            return ClinicalErrors.EncounterNotOpen.ToProblemResult();
        }

        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return ClinicalErrors.NoEmployeeRecord.ToProblemResult();
        }

        var order = await db.Set<PhysicianOrder>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == request.OrderId && o.EncounterId == encounterId, cancellationToken);
        if (order is null)
        {
            return ClinicalErrors.OrderNotFound.ToProblemResult();
        }

        if (order.OrderType != OrderType.Medication)
        {
            return ClinicalErrors.NotAMedicationOrder.ToProblemResult();
        }

        if (!order.IsActive)
        {
            return ClinicalErrors.MedicationOrderNotActive.ToProblemResult();
        }

        var now = time.GetUtcNow();
        var administeredAt = request.AdministeredAt?.ToUniversalTime() ?? now;
        if (administeredAt > now + FutureTolerance)
        {
            return ClinicalErrors.InvalidAdministeredAt("The time can't be in the future.").ToProblemResult();
        }

        if (administeredAt < order.OrderedAt)
        {
            return ClinicalErrors.InvalidAdministeredAt("The time can't be before the order was written.").ToProblemResult();
        }

        // The allergy warning isn't only on screen: with active allergies, the nurse confirms they checked them.
        var allergens = await ActiveAllergensAsync(db, encounter.PatientId, cancellationToken);
        if (allergens.Count > 0 && !request.AllergiesChecked)
        {
            return ClinicalErrors.AllergyCheckRequired.ToProblemResult();
        }

        var dose = new MedicationAdministration(order, employeeId, administeredAt, request.Outcome, request.DoseGiven, request.Notes);
        db.Add(dose);
        auditLog.Record(
            AuditEvents.Clinical.MedicationAdministered,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(MedicationAdministration), dose.Id),
                PatientId = encounter.PatientId,
                Details = new
                {
                    encounterId,
                    orderId = order.Id,
                    medicationName = order.MedicationName,
                    outcome = dose.Outcome,
                    allergiesChecked = allergens.Count > 0,
                    allergyMatches = AllergyCheck.Matches(order.MedicationName, allergens),
                },
            });
        await db.SaveChangesAsync(cancellationToken);

        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        var created = (await ToResponsesAsync(db, db.Set<MedicationAdministration>().Where(a => a.Id == dose.Id), reach, cancellationToken))[0];
        return TypedResults.Created($"/api/clinical/encounters/{encounterId}/medication-administrations", created);
    }

    /// <summary>Voids a dose entered in error, e.g. on the wrong order. It stays on the record, struck through.</summary>
    public static async Task<Results<Ok<MedicationAdministrationResponse>, ProblemHttpResult>> VoidAdministration(
        Guid encounterId,
        Guid id,
        VoidRecordRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var voided = await RecordVoiding.VoidAsync<MedicationAdministration>(
            db, permissions, staff, currentUser, auditLog, time, encounterId, id, request.Reason, "medication_administration", cancellationToken);
        if (!voided.IsSuccess)
        {
            return voided.Error.ToProblemResult();
        }

        await db.SaveChangesAsync(cancellationToken);

        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, db.Set<MedicationAdministration>().Where(a => a.Id == id), reach, cancellationToken))[0]);
    }

    /// <summary>
    /// Whether the caller may record doses on the encounter now: open, in their <c>clinical.medications.administer</c>
    /// scope, and they have an employee record to sign with.
    /// </summary>
    internal static async Task<bool> CanAdministerAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        Encounter encounter,
        CancellationToken cancellationToken)
    {
        if (encounter.Status != EncounterStatus.Open || await staff.MyEmployeeIdAsync(cancellationToken) is null)
        {
            return false;
        }

        var reach = await EncounterReach.ResolveAsync(db, permissions, staff, Perm.Clinical.MedicationsAdminister, cancellationToken);
        return await reach.Filter(db.Set<Encounter>().Where(e => e.Id == encounter.Id)).AnyAsync(cancellationToken);
    }

    /// <summary>The patient's active allergens, from the Patients module's allergy list.</summary>
    private static Task<List<string>> ActiveAllergensAsync(AppDbContext db, Guid patientId, CancellationToken cancellationToken) =>
        db.Set<PatientAllergy>().AsNoTracking()
            .Where(a => a.PatientId == patientId && a.Status == AllergyStatus.Active)
            .Select(a => a.Allergen)
            .ToListAsync(cancellationToken);

    private static async Task<List<MedicationAdministrationResponse>> ToResponsesAsync(
        AppDbContext db,
        IQueryable<MedicationAdministration> doses,
        VoidReach reach,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from a in doses.AsNoTracking()
            join e in db.Set<Employee>() on a.AdministeredBy equals e.Id
            orderby a.AdministeredAt descending, a.Id descending
            select new { Dose = a, Name = e.FirstName + " " + e.LastName })
            .ToListAsync(cancellationToken);
        var voidedBy = await RecordVoiding.VoidedByNamesAsync(db, rows.Select(r => r.Dose), cancellationToken);

        return
        [
            .. rows.Select(r => new MedicationAdministrationResponse(
                r.Dose.Id,
                r.Dose.OrderId,
                r.Dose.AdministeredAt,
                r.Name,
                r.Dose.Outcome,
                r.Dose.DoseGiven,
                r.Dose.Notes,
                RecordVoiding.ToResponse(r.Dose, voidedBy),
                reach.CanVoid(r.Dose))),
        ];
    }
}

using Hilom.SharedKernel.Auditing;

namespace Hilom.Clinical.Domain;

/// <summary>What happened when a dose was due.</summary>
public enum MedicationOutcome
{
    Given,

    /// <summary>Not given on purpose, e.g. blood pressure too low for the dose.</summary>
    Held,

    /// <summary>The patient declined it.</summary>
    Refused,

    /// <summary>The medication wasn't on hand.</summary>
    NotAvailable,
}

/// <summary>
/// One line of the medication administration record (docs/DATA_MODEL.md, medication_administrations): a dose of
/// an active medication order given, held, refused or not available, by the employee who was there. A dose
/// given says how much; one not given says why. Never edited: a mistake is voided with a reason.
/// </summary>
public sealed class MedicationAdministration : ClinicalRecord
{
    public const int DoseMaxLength = MedicationDetails.FieldMaxLength;
    public const int NotesMaxLength = 500;

    private MedicationAdministration()
    {
    }

    public MedicationAdministration(
        PhysicianOrder order,
        Guid administeredBy,
        DateTimeOffset administeredAt,
        MedicationOutcome outcome,
        string? doseGiven,
        string? notes)
        : base(order.EncounterId, order.PatientId)
    {
        OrderId = order.Id;
        AdministeredBy = administeredBy;
        AdministeredAt = administeredAt;
        Outcome = outcome;
        DoseGiven = outcome == MedicationOutcome.Given && !string.IsNullOrWhiteSpace(doseGiven) ? doseGiven.Trim() : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>The medication order this dose was due under.</summary>
    public Guid OrderId { get; private set; }

    public DateTimeOffset AdministeredAt { get; private set; }

    /// <summary>The employee who gave it, or recorded why not.</summary>
    public Guid AdministeredBy { get; private set; }

    public override Guid AuthorId => AdministeredBy;

    public MedicationOutcome Outcome { get; private set; }

    /// <summary>How much was given, e.g. "500 mg". Set only when <see cref="Outcome"/> is <c>given</c>.</summary>
    public string? DoseGiven { get; private set; }

    /// <summary>Required when the dose wasn't given: the reason. Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string? Notes { get; private set; }
}

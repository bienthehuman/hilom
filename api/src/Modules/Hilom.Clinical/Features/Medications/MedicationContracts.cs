using FluentValidation;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Voiding;

namespace Hilom.Clinical.Features.Medications;

/// <param name="AdministeredBy">The employee's name.</param>
/// <param name="DoseGiven">How much was given; null when it wasn't.</param>
/// <param name="Notes">Why it wasn't given (required then), or a remark on a dose given.</param>
/// <param name="Voided">Set when it was voided as entered in error; it stays on the record, struck through.</param>
/// <param name="CanVoid">Whether the caller may void it (<c>clinical.records.void</c>: all, or own = they recorded it).</param>
public sealed record MedicationAdministrationResponse(
    Guid Id,
    Guid OrderId,
    DateTimeOffset AdministeredAt,
    string AdministeredBy,
    MedicationOutcome Outcome,
    string? DoseGiven,
    string? Notes,
    VoidedResponse? Voided,
    bool CanVoid);

/// <summary>A medication order on the MAR, with every dose recorded against it.</summary>
/// <param name="OrderedBy">The physician's name.</param>
/// <param name="AllergyMatches">The patient's active allergens whose name matches the medication's. A name match only, so check every allergy too.</param>
/// <param name="CanRecord">Whether the caller may record a dose against it now: the order is active, the encounter is open, it's in their <c>clinical.medications.administer</c> scope and they have an employee record.</param>
/// <param name="Administrations">Newest first, voided ones included.</param>
public sealed record MarOrderResponse(
    Guid OrderId,
    string MedicationName,
    string Dose,
    string Route,
    string Frequency,
    string Details,
    OrderStatus Status,
    DateTimeOffset OrderedAt,
    string OrderedBy,
    IReadOnlyList<string> AllergyMatches,
    bool CanRecord,
    IReadOnlyList<MedicationAdministrationResponse> Administrations);

/// <param name="AdministeredAt">When the dose was due or given, for charting after the fact; now when left out.</param>
/// <param name="DoseGiven">Required when the outcome is <c>given</c>; ignored otherwise.</param>
/// <param name="Notes">Required when the outcome isn't <c>given</c>: why the dose wasn't given.</param>
/// <param name="AllergiesChecked">The nurse confirms they checked the patient's active allergies against the medication. Required when the patient has any.</param>
public sealed record RecordAdministrationRequest(
    Guid OrderId,
    MedicationOutcome Outcome,
    DateTimeOffset? AdministeredAt,
    string? DoseGiven,
    string? Notes,
    bool AllergiesChecked);

internal sealed class RecordAdministrationRequestValidator : AbstractValidator<RecordAdministrationRequest>
{
    public RecordAdministrationRequestValidator()
    {
        RuleFor(r => r.OrderId).NotEmpty().WithMessage("Choose the order.");
        RuleFor(r => r.Outcome).IsInEnum().WithMessage("Say whether the dose was given.");
        RuleFor(r => r.DoseGiven)
            .Must(d => !string.IsNullOrWhiteSpace(d)).When(r => r.Outcome == MedicationOutcome.Given)
            .WithMessage("Give the dose given, e.g. 500 mg.")
            .MaximumLength(MedicationAdministration.DoseMaxLength);

        // The reason a dose wasn't given is the point of the entry. Also a check constraint.
        RuleFor(r => r.Notes)
            .Must(n => !string.IsNullOrWhiteSpace(n)).When(r => r.Outcome != MedicationOutcome.Given)
            .WithMessage("Say why the dose wasn't given.")
            .MaximumLength(MedicationAdministration.NotesMaxLength);
    }
}

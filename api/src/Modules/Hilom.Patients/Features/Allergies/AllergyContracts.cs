using FluentValidation;
using Hilom.Patients.Domain;

namespace Hilom.Patients.Features.Allergies;

/// <param name="RecordedBy">Display name of who recorded it, if known.</param>
/// <param name="Version">Send back when changing it; a mismatch means someone else changed it meanwhile.</param>
public sealed record AllergyResponse(
    Guid Id,
    string Allergen,
    AllergyCategory Category,
    AllergySeverity Severity,
    string? Reaction,
    AllergyStatus Status,
    string? StatusNote,
    DateTimeOffset RecordedAt,
    string? RecordedBy,
    DateTimeOffset UpdatedAt,
    uint Version);

public interface IAllergyFields
{
    string Allergen { get; }

    AllergyCategory? Category { get; }

    AllergySeverity? Severity { get; }

    string? Reaction { get; }
}

public sealed record RecordAllergyRequest(string Allergen, AllergyCategory? Category, AllergySeverity? Severity, string? Reaction)
    : IAllergyFields;

public sealed record UpdateAllergyRequest(
    string Allergen,
    AllergyCategory? Category,
    AllergySeverity? Severity,
    string? Reaction,
    uint Version) : IAllergyFields;

/// <param name="Status"><c>resolved</c> or <c>entered_in_error</c>.</param>
/// <param name="Note">Why. Required for entered in error.</param>
public sealed record ChangeAllergyStatusRequest(AllergyStatus? Status, string? Note, uint Version);

internal static class AllergyFieldRules
{
    public static AllergyDetails ToDetails(this IAllergyFields request) =>
        new(request.Allergen, request.Category!.Value, request.Severity!.Value, request.Reaction);

    public static void AddAllergyRules<T>(this AbstractValidator<T> validator)
        where T : IAllergyFields
    {
        validator.RuleFor(r => r.Allergen)
            .NotEmpty().WithMessage("Enter what the patient is allergic to.")
            .MaximumLength(PatientAllergy.AllergenMaxLength);
        validator.RuleFor(r => r.Category).NotNull().WithMessage("Choose a category.");
        validator.RuleFor(r => r.Severity).NotNull().WithMessage("Choose a severity.");
        validator.RuleFor(r => r.Reaction).MaximumLength(PatientAllergy.ReactionMaxLength);
    }
}

internal sealed class RecordAllergyRequestValidator : AbstractValidator<RecordAllergyRequest>
{
    public RecordAllergyRequestValidator() => this.AddAllergyRules();
}

internal sealed class UpdateAllergyRequestValidator : AbstractValidator<UpdateAllergyRequest>
{
    public UpdateAllergyRequestValidator() => this.AddAllergyRules();
}

internal sealed class ChangeAllergyStatusRequestValidator : AbstractValidator<ChangeAllergyStatusRequest>
{
    public ChangeAllergyStatusRequestValidator()
    {
        RuleFor(r => r.Status)
            .NotNull().WithMessage("Choose resolved or entered in error.")
            .NotEqual(AllergyStatus.Active).WithMessage("Choose resolved or entered in error.");
        RuleFor(r => r.Note)
            .NotEmpty().WithMessage("Say what was wrong with this entry.")
            .When(r => r.Status == AllergyStatus.EnteredInError);
        RuleFor(r => r.Note).MaximumLength(PatientAllergy.NoteMaxLength);
    }
}

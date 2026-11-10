using FluentValidation;
using Hilom.Infrastructure.Configuration;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Domain;

namespace Hilom.Patients.Features.Registry;

public sealed record PatientSummaryResponse(
    Guid Id,
    string Mrn,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly BirthDate,
    Sex Sex,
    string ContactNumber,
    DateTimeOffset RegisteredAt);

/// <param name="RegisteredBy">Display name of the staff member who registered the patient, if known.</param>
/// <param name="Version">Send back when saving; a mismatch means someone else changed the patient meanwhile.</param>
public sealed record PatientResponse(
    Guid Id,
    string Mrn,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly BirthDate,
    Sex Sex,
    CivilStatus? CivilStatus,
    string? Religion,
    string Address,
    string ContactNumber,
    string EmergencyContactName,
    string EmergencyContactRelation,
    string EmergencyContactNumber,
    DateTimeOffset RegisteredAt,
    string? RegisteredBy,
    uint Version);

/// <summary>The fields shared by register and update.</summary>
public interface IPatientFields
{
    string FirstName { get; }

    string? MiddleName { get; }

    string LastName { get; }

    DateOnly? BirthDate { get; }

    Sex? Sex { get; }

    CivilStatus? CivilStatus { get; }

    string? Religion { get; }

    string Address { get; }

    string ContactNumber { get; }

    string EmergencyContactName { get; }

    string EmergencyContactRelation { get; }

    string EmergencyContactNumber { get; }
}

public sealed record RegisterPatientRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly? BirthDate,
    Sex? Sex,
    CivilStatus? CivilStatus,
    string? Religion,
    string Address,
    string ContactNumber,
    string EmergencyContactName,
    string EmergencyContactRelation,
    string EmergencyContactNumber) : IPatientFields;

public sealed record UpdatePatientRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly? BirthDate,
    Sex? Sex,
    CivilStatus? CivilStatus,
    string? Religion,
    string Address,
    string ContactNumber,
    string EmergencyContactName,
    string EmergencyContactRelation,
    string EmergencyContactNumber,
    uint Version) : IPatientFields;

internal static class PatientFieldRules
{
    /// <summary>Request fields → domain details. Call only after validation passed.</summary>
    public static PatientDetails ToDetails(this IPatientFields request) =>
        new(
            request.FirstName,
            request.MiddleName,
            request.LastName,
            request.BirthDate!.Value,
            request.Sex!.Value,
            request.CivilStatus,
            request.Religion,
            request.Address,
            request.ContactNumber,
            request.EmergencyContactName,
            request.EmergencyContactRelation,
            request.EmergencyContactNumber);

    public static void AddPatientRules<T>(this AbstractValidator<T> validator, HospitalClock clock)
        where T : IPatientFields
    {
        validator.RuleFor(r => r.FirstName).NotEmpty().WithMessage("Enter the first name.").MaximumLength(Patient.NameMaxLength);
        validator.RuleFor(r => r.MiddleName).MaximumLength(Patient.NameMaxLength);
        validator.RuleFor(r => r.LastName).NotEmpty().WithMessage("Enter the last name.").MaximumLength(Patient.NameMaxLength);
        validator.RuleFor(r => r.BirthDate)
            .NotNull().WithMessage("Enter the date of birth.")
            .Must(date => date <= clock.Today).WithMessage("The date of birth can't be in the future.")
            .Must(date => date >= new DateOnly(1900, 1, 1)).WithMessage("Check the year.");
        validator.RuleFor(r => r.Sex).NotNull().WithMessage("Choose one.");
        validator.RuleFor(r => r.Religion).MaximumLength(Patient.ReligionMaxLength);
        validator.RuleFor(r => r.Address).NotEmpty().WithMessage("Enter the home address.").MaximumLength(Patient.AddressMaxLength);
        validator.RuleFor(r => r.ContactNumber).NotEmpty().WithMessage("Enter a contact number.").MaximumLength(Patient.ContactMaxLength);
        validator.RuleFor(r => r.EmergencyContactName)
            .NotEmpty().WithMessage("Enter who to call in an emergency.")
            .MaximumLength(Patient.EmergencyNameMaxLength);
        validator.RuleFor(r => r.EmergencyContactRelation)
            .NotEmpty().WithMessage("Enter how they're related, e.g. mother.")
            .MaximumLength(Patient.RelationMaxLength);
        validator.RuleFor(r => r.EmergencyContactNumber)
            .NotEmpty().WithMessage("Enter their contact number.")
            .MaximumLength(Patient.ContactMaxLength);
    }
}

internal sealed class RegisterPatientRequestValidator : AbstractValidator<RegisterPatientRequest>
{
    public RegisterPatientRequestValidator(HospitalClock clock) => this.AddPatientRules(clock);
}

internal sealed class UpdatePatientRequestValidator : AbstractValidator<UpdatePatientRequest>
{
    public UpdatePatientRequestValidator(HospitalClock clock) => this.AddPatientRules(clock);
}

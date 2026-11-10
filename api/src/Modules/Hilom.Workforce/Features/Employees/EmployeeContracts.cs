using FluentValidation;
using Hilom.Infrastructure.Configuration;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Features.Employees;

/// <summary>A department, position or specialization as shown on an employee. Inactive ones still show here.</summary>
public sealed record LookupRef(Guid Id, string Name, bool IsActive);

/// <param name="HasAccount">The employee can sign in (they accepted an invite).</param>
public sealed record EmployeeSummaryResponse(
    Guid Id,
    string EmployeeNo,
    string FirstName,
    string? MiddleName,
    string LastName,
    LookupRef Department,
    LookupRef Position,
    ProfessionalType ProfessionalType,
    EmployeeStatus Status,
    bool HasAccount);

public sealed record PhysicianProfileResponse(LookupRef Specialization, bool AcceptsAppointments, int ConsultationMinutes);

/// <param name="Version">Send back when saving; a mismatch means someone else changed the employee meanwhile.</param>
public sealed record EmployeeResponse(
    Guid Id,
    string EmployeeNo,
    string FirstName,
    string? MiddleName,
    string LastName,
    Sex Sex,
    DateOnly BirthDate,
    string? ContactNumber,
    string? Email,
    LookupRef Department,
    LookupRef Position,
    ProfessionalType ProfessionalType,
    string? LicenseNumber,
    EmploymentType EmploymentType,
    DateOnly HiredOn,
    EmployeeStatus Status,
    DateOnly? SeparatedOn,
    PhysicianProfileResponse? Physician,
    bool HasAccount,
    uint Version);

public sealed record PhysicianProfileRequest(Guid SpecializationId, bool AcceptsAppointments, int? ConsultationMinutes);

/// <summary>The fields shared by create and update.</summary>
public interface IEmployeeFields
{
    string FirstName { get; }

    string? MiddleName { get; }

    string LastName { get; }

    Sex? Sex { get; }

    DateOnly? BirthDate { get; }

    string? ContactNumber { get; }

    string? Email { get; }

    Guid DepartmentId { get; }

    Guid PositionId { get; }

    ProfessionalType? ProfessionalType { get; }

    string? LicenseNumber { get; }

    EmploymentType? EmploymentType { get; }

    DateOnly? HiredOn { get; }

    /// <summary>Required for physicians, and must be left out for everyone else.</summary>
    PhysicianProfileRequest? Physician { get; }
}

public sealed record CreateEmployeeRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    Sex? Sex,
    DateOnly? BirthDate,
    string? ContactNumber,
    string? Email,
    Guid DepartmentId,
    Guid PositionId,
    ProfessionalType? ProfessionalType,
    string? LicenseNumber,
    EmploymentType? EmploymentType,
    DateOnly? HiredOn,
    PhysicianProfileRequest? Physician) : IEmployeeFields;

public sealed record UpdateEmployeeRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    Sex? Sex,
    DateOnly? BirthDate,
    string? ContactNumber,
    string? Email,
    Guid DepartmentId,
    Guid PositionId,
    ProfessionalType? ProfessionalType,
    string? LicenseNumber,
    EmploymentType? EmploymentType,
    DateOnly? HiredOn,
    PhysicianProfileRequest? Physician,
    uint Version) : IEmployeeFields;

/// <param name="SeparatedOn">Last day of work. Required when separating; ignored otherwise.</param>
public sealed record ChangeEmployeeStatusRequest(EmployeeStatus? Status, DateOnly? SeparatedOn, uint Version);

internal static class EmployeeFieldRules
{
    public const int DefaultConsultationMinutes = 30;

    /// <summary>Request fields → domain details. Call only after validation passed.</summary>
    public static EmployeeDetails ToDetails(this IEmployeeFields request) =>
        new(
            request.FirstName,
            request.MiddleName,
            request.LastName,
            request.Sex!.Value,
            request.BirthDate!.Value,
            request.ContactNumber,
            request.Email,
            request.DepartmentId,
            request.PositionId,
            request.ProfessionalType!.Value,
            request.LicenseNumber,
            request.EmploymentType!.Value,
            request.HiredOn!.Value,
            request.Physician is { } physician
                ? new PhysicianProfile(
                    physician.SpecializationId,
                    physician.AcceptsAppointments,
                    physician.ConsultationMinutes ?? DefaultConsultationMinutes)
                : null);

    public static void AddEmployeeRules<T>(this AbstractValidator<T> validator, HospitalClock clock)
        where T : IEmployeeFields
    {
        validator.RuleFor(r => r.FirstName).NotEmpty().WithMessage("Enter the first name.").MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(r => r.MiddleName).MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(r => r.LastName).NotEmpty().WithMessage("Enter the last name.").MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(r => r.Sex).NotNull().WithMessage("Choose one.");
        validator.RuleFor(r => r.BirthDate)
            .NotNull().WithMessage("Enter the date of birth.")
            .Must(date => date < clock.Today).WithMessage("The date of birth must be in the past.")
            .Must(date => date >= new DateOnly(1900, 1, 1)).WithMessage("Check the year.");
        validator.RuleFor(r => r.ContactNumber).MaximumLength(Employee.ContactMaxLength);
        validator.RuleFor(r => r.Email)
            .EmailAddress().WithMessage("Enter an email address like name@example.com.")
            .MaximumLength(Employee.EmailMaxLength)
            .When(r => !string.IsNullOrWhiteSpace(r.Email));
        validator.RuleFor(r => r.DepartmentId).NotEmpty().WithMessage("Choose a department.");
        validator.RuleFor(r => r.PositionId).NotEmpty().WithMessage("Choose a position.");
        validator.RuleFor(r => r.ProfessionalType).NotNull().WithMessage("Choose a type.");
        validator.RuleFor(r => r.LicenseNumber).MaximumLength(Employee.LicenseMaxLength);
        validator.RuleFor(r => r.EmploymentType).NotNull().WithMessage("Choose an employment type.");
        validator.RuleFor(r => r.HiredOn)
            .NotNull().WithMessage("Enter the hire date.")
            .GreaterThan(r => r.BirthDate).When(r => r.BirthDate is not null).WithMessage("The hire date must be after the date of birth.");
        validator.RuleFor(r => r.Physician!.SpecializationId)
            .NotEmpty().WithMessage("Choose a specialization.")
            .When(r => r.Physician is not null)
            .OverridePropertyName("physician.specializationId");
        validator.RuleFor(r => r.Physician!.ConsultationMinutes)
            .InclusiveBetween(PhysicianProfile.MinConsultationMinutes, PhysicianProfile.MaxConsultationMinutes)
            .WithMessage($"Use {PhysicianProfile.MinConsultationMinutes} to {PhysicianProfile.MaxConsultationMinutes} minutes.")
            .When(r => r.Physician?.ConsultationMinutes is not null)
            .OverridePropertyName("physician.consultationMinutes");
    }
}

internal sealed class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator(HospitalClock clock) => this.AddEmployeeRules(clock);
}

internal sealed class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator(HospitalClock clock) => this.AddEmployeeRules(clock);
}

internal sealed class ChangeEmployeeStatusRequestValidator : AbstractValidator<ChangeEmployeeStatusRequest>
{
    public ChangeEmployeeStatusRequestValidator() =>
        RuleFor(r => r.Status).NotNull().WithMessage("Choose a status.");
}

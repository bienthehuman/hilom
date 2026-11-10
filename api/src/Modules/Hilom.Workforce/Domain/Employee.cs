using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Workforce.Domain;

public enum Sex
{
    Male,
    Female,
}

public enum ProfessionalType
{
    Physician,
    Nurse,
    AlliedHealth,
    Administrative,
}

public enum EmploymentType
{
    FullTime,
    PartTime,
    Contract,
}

public enum EmployeeStatus
{
    Active,
    OnLeave,
    Separated,
}

/// <summary>Everything about an employee that HR edits in one form. Status has its own action.</summary>
public sealed record EmployeeDetails(
    string FirstName,
    string? MiddleName,
    string LastName,
    Sex Sex,
    DateOnly BirthDate,
    string? ContactNumber,
    string? Email,
    Guid DepartmentId,
    Guid PositionId,
    ProfessionalType ProfessionalType,
    string? LicenseNumber,
    EmploymentType EmploymentType,
    DateOnly HiredOn,
    PhysicianProfile? Physician);

/// <summary>Booking details for a physician. Only physicians have one.</summary>
public sealed record PhysicianProfile(Guid SpecializationId, bool AcceptsAppointments, int ConsultationMinutes)
{
    public const int MinConsultationMinutes = 5;
    public const int MaxConsultationMinutes = 240;
}

/// <summary>Someone who works at the hospital, whether or not they sign in (docs/DATA_MODEL.md).</summary>
public sealed class Employee : Entity
{
    public const int NameMaxLength = 100;
    public const int LicenseMaxLength = 50;
    public const int ContactMaxLength = 30;
    public const int EmailMaxLength = 320;

    private Employee()
    {
    }

    private Employee(string employeeNo) => EmployeeNo = employeeNo;

    /// <summary>Human-readable number, e.g. <c>EMP-2026-0042</c>. Never changes.</summary>
    public string EmployeeNo { get; private set; } = "";

    /// <summary>The sign-in account, once the employee accepts an invite. At most one employee per user.</summary>
    public Guid? UserId { get; private set; }

    public string FirstName { get; private set; } = "";

    public string? MiddleName { get; private set; }

    public string LastName { get; private set; } = "";

    public Sex Sex { get; private set; }

    public DateOnly BirthDate { get; private set; }

    public string? ContactNumber { get; private set; }

    /// <summary>Work email. Separate from the sign-in email.</summary>
    public string? Email { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid PositionId { get; private set; }

    public ProfessionalType ProfessionalType { get; private set; }

    /// <summary>Professional license (e.g. PRC number). Required for physicians and nurses.</summary>
    public string? LicenseNumber { get; private set; }

    public EmploymentType EmploymentType { get; private set; }

    public DateOnly HiredOn { get; private set; }

    public EmployeeStatus Status { get; private set; } = EmployeeStatus.Active;

    /// <summary>Set exactly when the status is <see cref="EmployeeStatus.Separated"/>.</summary>
    public DateOnly? SeparatedOn { get; private set; }

    public PhysicianProfile? Physician { get; private set; }

    public static bool RequiresLicense(ProfessionalType type) =>
        type is ProfessionalType.Physician or ProfessionalType.Nurse;

    public static Result<Employee> Create(string employeeNo, EmployeeDetails details)
    {
        var employee = new Employee(employeeNo);
        var result = employee.Update(details);
        return result.IsSuccess ? Result.Success(employee) : Result.Failure<Employee>(result.Error);
    }

    public Result Update(EmployeeDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var errors = new Dictionary<string, string[]>();
        var license = Clean(details.LicenseNumber);
        if (RequiresLicense(details.ProfessionalType) && license is null)
        {
            errors["licenseNumber"] = ["Physicians and nurses need a license number."];
        }

        if (details.ProfessionalType == ProfessionalType.Physician && details.Physician is null)
        {
            errors["physician"] = ["Add the physician's specialization."];
        }

        if (details.ProfessionalType != ProfessionalType.Physician && details.Physician is not null)
        {
            errors["physician"] = ["Only physicians have a physician profile."];
        }

        if (SeparatedOn is { } separatedOn && details.HiredOn > separatedOn)
        {
            errors["hiredOn"] = ["The hire date can't be after the separation date."];
        }

        if (errors.Count > 0)
        {
            return Result.Failure(WorkforceErrors.InvalidEmployee(errors));
        }

        FirstName = details.FirstName.Trim();
        MiddleName = Clean(details.MiddleName);
        LastName = details.LastName.Trim();
        Sex = details.Sex;
        BirthDate = details.BirthDate;
        ContactNumber = Clean(details.ContactNumber);
        Email = Clean(details.Email);
        DepartmentId = details.DepartmentId;
        PositionId = details.PositionId;
        ProfessionalType = details.ProfessionalType;
        LicenseNumber = license;
        EmploymentType = details.EmploymentType;
        HiredOn = details.HiredOn;
        Physician = details.Physician;
        return Result.Success();
    }

    /// <summary>Active, on leave, or separated (left the hospital, on <paramref name="separatedOn"/>).</summary>
    public Result ChangeStatus(EmployeeStatus status, DateOnly? separatedOn)
    {
        if (status == EmployeeStatus.Separated)
        {
            if (separatedOn is null)
            {
                return Result.Failure(WorkforceErrors.InvalidEmployee("separatedOn", "Enter the last day of work."));
            }

            if (separatedOn < HiredOn)
            {
                return Result.Failure(WorkforceErrors.InvalidEmployee("separatedOn", "The separation date can't be before the hire date."));
            }
        }

        Status = status;
        SeparatedOn = status == EmployeeStatus.Separated ? separatedOn : null;
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

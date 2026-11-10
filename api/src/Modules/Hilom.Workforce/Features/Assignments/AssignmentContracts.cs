using FluentValidation;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Features.Assignments;

public enum AssignmentState
{
    Upcoming,
    Current,
    Past,
}

public sealed record AssignedEmployee(Guid Id, string EmployeeNo, string FirstName, string LastName, ProfessionalType ProfessionalType);

public sealed record AssignedWard(Guid Id, string Code, string Name);

/// <param name="LastDay">Last day on the ward (inclusive), or null when open-ended.</param>
/// <param name="State">Relative to today in hospital time.</param>
/// <param name="Version">Send back when ending the assignment.</param>
public sealed record AssignmentResponse(
    Guid Id,
    AssignedEmployee Employee,
    AssignedWard Ward,
    DateOnly StartsOn,
    DateOnly? LastDay,
    AssignmentState State,
    string? Reason,
    string? AssignedBy,
    uint Version);

/// <param name="StartsOn">First day on the ward. Defaults to today.</param>
/// <param name="LastDay">Last day on the ward, if known. Leave empty for open-ended.</param>
public sealed record AssignWardRequest(Guid EmployeeId, Guid WardId, DateOnly? StartsOn, DateOnly? LastDay, string? Reason);

/// <param name="LastDay">Last day on the ward (inclusive). To move someone today, end with yesterday.</param>
public sealed record EndAssignmentRequest(DateOnly? LastDay, uint Version);

internal sealed class AssignWardRequestValidator : AbstractValidator<AssignWardRequest>
{
    public AssignWardRequestValidator()
    {
        RuleFor(r => r.EmployeeId).NotEmpty().WithMessage("Choose who to assign.");
        RuleFor(r => r.WardId).NotEmpty().WithMessage("Choose a ward.");
        RuleFor(r => r.Reason).MaximumLength(WardAssignment.ReasonMaxLength);
    }
}

internal sealed class EndAssignmentRequestValidator : AbstractValidator<EndAssignmentRequest>
{
    public EndAssignmentRequestValidator() => RuleFor(r => r.LastDay).NotNull().WithMessage("Enter the last day on the ward.");
}

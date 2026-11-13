using FluentValidation;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Features.Assignments;

namespace Hilom.Workforce.Features.Roster;

public sealed record RosterShift(Guid Id, string Code, string Name, TimeOnly StartsAt, TimeOnly EndsAt, bool CrossesMidnight, ShiftColor Color);

/// <param name="Version">Send back when changing the shift.</param>
public sealed record RosterEntryResponse(
    Guid Id,
    Guid EmployeeId,
    AssignedWard Ward,
    RosterShift Shift,
    DateOnly DutyDate,
    RosterStatus Status,
    uint Version);

/// <summary>A period someone is assigned to the ward. Days outside these can't be rostered.</summary>
public sealed record AssignedPeriod(DateOnly StartsOn, DateOnly? LastDay);

public sealed record RosterStaff(AssignedEmployee Employee, IReadOnlyList<AssignedPeriod> Assignments);

/// <summary>One ward's roster for a range of days: who is (or was) assigned, and their entries.</summary>
public sealed record WardRosterResponse(
    AssignedWard Ward,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<RosterStaff> Staff,
    IReadOnlyList<RosterEntryResponse> Entries);

/// <param name="Linked">False when the signed-in account isn't linked to an employee record, so there's no schedule.</param>
public sealed record MyScheduleResponse(bool Linked, DateOnly From, DateOnly To, IReadOnlyList<RosterEntryResponse> Entries);

public sealed record AddRosterEntryRequest(Guid EmployeeId, Guid WardId, Guid ShiftId, DateOnly? DutyDate);

public sealed record ChangeRosterShiftRequest(Guid ShiftId, uint Version);

internal sealed class AddRosterEntryRequestValidator : AbstractValidator<AddRosterEntryRequest>
{
    public AddRosterEntryRequestValidator()
    {
        RuleFor(r => r.EmployeeId).NotEmpty().WithMessage("Choose who is on duty.");
        RuleFor(r => r.WardId).NotEmpty().WithMessage("Choose a ward.");
        RuleFor(r => r.ShiftId).NotEmpty().WithMessage("Choose a shift.");
        RuleFor(r => r.DutyDate).NotNull().WithMessage("Choose the day.");
    }
}

internal sealed class ChangeRosterShiftRequestValidator : AbstractValidator<ChangeRosterShiftRequest>
{
    public ChangeRosterShiftRequestValidator() => RuleFor(r => r.ShiftId).NotEmpty().WithMessage("Choose a shift.");
}

/// <summary>How many staff are on duty on one day, per ward in reach (the Chief Nurse's staffing tile).</summary>
public sealed record StaffingResponse(DateOnly Date, IReadOnlyList<WardStaffing> Wards);

/// <param name="OnDuty">Scheduled entries that day, all shifts.</param>
/// <param name="Shifts">Only the shifts someone is on, earliest start first.</param>
public sealed record WardStaffing(AssignedWard Ward, int OnDuty, IReadOnlyList<ShiftStaffing> Shifts);

public sealed record ShiftStaffing(Guid ShiftId, string Code, int OnDuty);

using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;
using NpgsqlTypes;

namespace Hilom.Workforce.Domain;

/// <summary>
/// An employee's ward over a period (docs/DATA_MODEL.md). This drives the <c>ward</c> scope: what a nurse
/// sees depends on the ward they're assigned to today. One ward at a time per employee, enforced by an
/// exclusion constraint.
/// </summary>
/// <remarks>
/// Stored as a half-open <c>daterange</c> <c>[startsOn, lastDay + 1)</c>, open-ended when there's no last day.
/// The API speaks in <see cref="StartsOn"/> and an inclusive <see cref="LastDay"/>, as staff do.
/// </remarks>
public sealed class WardAssignment : Entity
{
    public const int ReasonMaxLength = 300;

    private WardAssignment()
    {
    }

    private WardAssignment(Guid employeeId, Guid wardId, NpgsqlRange<DateOnly> period, string? reason, Guid? assignedBy)
    {
        EmployeeId = employeeId;
        WardId = wardId;
        Period = period;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        AssignedBy = assignedBy;
    }

    public Guid EmployeeId { get; private set; }

    public Guid WardId { get; private set; }

    public NpgsqlRange<DateOnly> Period { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>The user who made the assignment.</summary>
    public Guid? AssignedBy { get; private set; }

    public DateOnly StartsOn => Period.LowerBound;

    /// <summary>The last day on the ward, or null when open-ended.</summary>
    public DateOnly? LastDay => Period.UpperBoundInfinite ? null : Period.UpperBound.AddDays(-1);

    public bool IsOn(DateOnly day) => day >= StartsOn && (LastDay is null || day <= LastDay);

    /// <summary>The period as stored: <c>[startsOn, lastDay + 1)</c>, or with no upper bound.</summary>
    public static NpgsqlRange<DateOnly> PeriodOf(DateOnly startsOn, DateOnly? lastDay) =>
        lastDay is { } last
            ? new NpgsqlRange<DateOnly>(startsOn, true, false, last.AddDays(1), false, false)
            : new NpgsqlRange<DateOnly>(startsOn, true, false, default, false, true);

    public static Result<WardAssignment> Create(
        Guid employeeId,
        Guid wardId,
        DateOnly startsOn,
        DateOnly? lastDay,
        string? reason,
        Guid? assignedBy)
    {
        if (lastDay < startsOn)
        {
            return Result.Failure<WardAssignment>(WorkforceErrors.InvalidAssignment("lastDay", "The last day can't be before the start."));
        }

        return Result.Success(new WardAssignment(employeeId, wardId, PeriodOf(startsOn, lastDay), reason, assignedBy));
    }

    /// <summary>Sets the last day on the ward. Assignments can be shortened, not extended past an existing end.</summary>
    public Result End(DateOnly lastDay)
    {
        if (lastDay < StartsOn)
        {
            return Result.Failure(WorkforceErrors.InvalidAssignment("lastDay", "The last day can't be before the start."));
        }

        if (LastDay is { } current && lastDay > current)
        {
            return Result.Failure(WorkforceErrors.InvalidAssignment("lastDay", "This assignment already ends earlier. Make a new assignment instead."));
        }

        Period = PeriodOf(StartsOn, lastDay);
        return Result.Success();
    }
}

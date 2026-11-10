using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;
using NpgsqlTypes;

namespace Hilom.Scheduling.Domain;

public enum SlotStatus
{
    /// <summary>Offered: patients can book it (slice 2.6).</summary>
    Open,

    /// <summary>Withdrawn by the physician or front desk. Kept as history.</summary>
    Closed,
}

/// <summary>
/// Time a physician offers for appointments (docs/DATA_MODEL.md, scheduling). A physician's open slots never
/// overlap: the database enforces it with an exclusion constraint. Whether a slot is booked comes from
/// appointments, not from the slot.
/// </summary>
public sealed class AvailabilitySlot : Entity
{
    private AvailabilitySlot()
    {
    }

    private AvailabilitySlot(Guid physicianId, NpgsqlRange<DateTime> during)
    {
        PhysicianId = physicianId;
        During = during;
        Status = SlotStatus.Open;
    }

    /// <summary>The physician's employee id (their physician profile).</summary>
    public Guid PhysicianId { get; private set; }

    /// <summary>Half-open UTC range: starts at <see cref="StartsAt"/>, ends just before <see cref="EndsAt"/>.</summary>
    public NpgsqlRange<DateTime> During { get; private set; }

    public SlotStatus Status { get; private set; }

    public DateTimeOffset StartsAt => new(During.LowerBound, TimeSpan.Zero);

    public DateTimeOffset EndsAt => new(During.UpperBound, TimeSpan.Zero);

    public static NpgsqlRange<DateTime> RangeOf(DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        new(startsAt.UtcDateTime, true, endsAt.UtcDateTime, false);

    public static Result<AvailabilitySlot> Create(Guid physicianId, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        endsAt > startsAt
            ? Result.Success(new AvailabilitySlot(physicianId, RangeOf(startsAt, endsAt)))
            : Result.Failure<AvailabilitySlot>(SchedulingErrors.InvalidPattern("endTime", "The end time must be after the start time."));

    public Result Close()
    {
        if (Status != SlotStatus.Open)
        {
            return Result.Failure(SchedulingErrors.SlotNotOpen);
        }

        Status = SlotStatus.Closed;
        return Result.Success();
    }
}

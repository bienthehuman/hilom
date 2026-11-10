using Hilom.SharedKernel.Domain;

namespace Hilom.Workforce.Domain;

public enum RosterStatus
{
    /// <summary>On duty as planned.</summary>
    Scheduled,

    /// <summary>Given away in an approved shift swap (slice 1.8).</summary>
    SwappedOut,

    /// <summary>Didn't work the shift.</summary>
    Absent,
}

/// <summary>
/// One person on one shift, on one ward, on one day (docs/DATA_MODEL.md). One scheduled shift per person per
/// day, enforced by a partial unique index. Planning data, so removing an entry deletes it; the audit log
/// keeps what it was.
/// </summary>
public sealed class RosterEntry : Entity
{
    private RosterEntry()
    {
    }

    public RosterEntry(Guid employeeId, Guid wardId, Guid shiftId, DateOnly dutyDate)
    {
        EmployeeId = employeeId;
        WardId = wardId;
        ShiftId = shiftId;
        DutyDate = dutyDate;
    }

    public Guid EmployeeId { get; private set; }

    public Guid WardId { get; private set; }

    public Guid ShiftId { get; private set; }

    public DateOnly DutyDate { get; private set; }

    public RosterStatus Status { get; private set; } = RosterStatus.Scheduled;

    public void ChangeShift(Guid shiftId) => ShiftId = shiftId;

    /// <summary>Given away in an approved swap. The new holder gets a new entry.</summary>
    public void MarkSwappedOut() => Status = RosterStatus.SwappedOut;
}

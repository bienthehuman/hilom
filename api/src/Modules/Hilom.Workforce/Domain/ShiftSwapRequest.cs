using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Workforce.Domain;

public enum SwapStatus
{
    PendingTarget,
    PendingApproval,
    Approved,
    DeclinedByTarget,
    Rejected,
    Cancelled,
}

/// <summary>
/// A request to exchange two shifts on the same ward (docs/DATA_MODEL.md): the colleague accepts, then
/// someone with <c>workforce.swaps.approve</c> on that ward approves. The swap keeps a snapshot of both
/// shifts, so its history stays readable even if a roster entry is later removed.
/// </summary>
public sealed class ShiftSwapRequest : Entity
{
    public const int NoteMaxLength = 300;

    private ShiftSwapRequest()
    {
    }

    public ShiftSwapRequest(RosterEntry requester, RosterEntry target, string? message)
    {
        RequesterEntryId = requester.Id;
        RequesterEmployeeId = requester.EmployeeId;
        RequesterDutyDate = requester.DutyDate;
        RequesterShiftId = requester.ShiftId;
        TargetEntryId = target.Id;
        TargetEmployeeId = target.EmployeeId;
        TargetDutyDate = target.DutyDate;
        TargetShiftId = target.ShiftId;
        WardId = requester.WardId;
        Message = Clean(message);
    }

    public Guid? RequesterEntryId { get; private set; }

    public Guid RequesterEmployeeId { get; private set; }

    public DateOnly RequesterDutyDate { get; private set; }

    public Guid RequesterShiftId { get; private set; }

    public Guid? TargetEntryId { get; private set; }

    public Guid TargetEmployeeId { get; private set; }

    public DateOnly TargetDutyDate { get; private set; }

    public Guid TargetShiftId { get; private set; }

    public Guid WardId { get; private set; }

    public string? Message { get; private set; }

    public SwapStatus Status { get; private set; } = SwapStatus.PendingTarget;

    public DateTimeOffset? TargetRespondedAt { get; private set; }

    public string? TargetResponse { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewNote { get; private set; }

    public bool IsOpen => Status is SwapStatus.PendingTarget or SwapStatus.PendingApproval;

    /// <summary>The entries still hold the shifts the swap was asked for.</summary>
    public bool Matches(RosterEntry requester, RosterEntry target) =>
        requester.Status == RosterStatus.Scheduled && target.Status == RosterStatus.Scheduled
        && requester.EmployeeId == RequesterEmployeeId && requester.DutyDate == RequesterDutyDate && requester.ShiftId == RequesterShiftId
        && target.EmployeeId == TargetEmployeeId && target.DutyDate == TargetDutyDate && target.ShiftId == TargetShiftId;

    public Result Accept(DateTimeOffset now, string? note) => Respond(now, note, SwapStatus.PendingApproval);

    public Result Decline(DateTimeOffset now, string? note) => Respond(now, note, SwapStatus.DeclinedByTarget);

    public Result Cancel()
    {
        if (!IsOpen)
        {
            return Result.Failure(WorkforceErrors.SwapNotWaiting);
        }

        Status = SwapStatus.Cancelled;
        return Result.Success();
    }

    public Result Approve(Guid? reviewer, DateTimeOffset now, string? note) => Review(reviewer, now, note, SwapStatus.Approved);

    public Result Reject(Guid? reviewer, DateTimeOffset now, string? note) => Review(reviewer, now, note, SwapStatus.Rejected);

    private Result Respond(DateTimeOffset now, string? note, SwapStatus next)
    {
        if (Status != SwapStatus.PendingTarget)
        {
            return Result.Failure(WorkforceErrors.SwapNotWaiting);
        }

        Status = next;
        TargetRespondedAt = now;
        TargetResponse = Clean(note);
        return Result.Success();
    }

    private Result Review(Guid? reviewer, DateTimeOffset now, string? note, SwapStatus next)
    {
        if (Status != SwapStatus.PendingApproval)
        {
            return Result.Failure(WorkforceErrors.SwapNotWaiting);
        }

        Status = next;
        ReviewedBy = reviewer;
        ReviewedAt = now;
        ReviewNote = Clean(note);
        return Result.Success();
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

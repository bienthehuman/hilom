using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Scheduling.Domain;

public enum AppointmentStatus
{
    /// <summary>Booked; waiting for the physician to confirm (slice 2.7).</summary>
    Requested,
    Confirmed,
    CheckedIn,
    Completed,
    Declined,
    Cancelled,
    NoShow,
}

/// <summary>
/// A patient booked into a slot (docs/DATA_MODEL.md, scheduling). One request holds the slot until it's declined or
/// cancelled: a partial unique index allows one live appointment per slot. No waitlist.
/// </summary>
public sealed class Appointment : Entity, IPatientRecord
{
    public const int ReasonMaxLength = 500;
    public const int NoteMaxLength = 500;

    /// <summary>Statuses that hold the slot.</summary>
    public static readonly IReadOnlyList<AppointmentStatus> Live =
        [AppointmentStatus.Requested, AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn, AppointmentStatus.Completed];

    private Appointment()
    {
    }

    public Appointment(Guid slotId, Guid patientId, Guid bookedBy, string reason)
    {
        SlotId = slotId;
        PatientId = patientId;
        BookedBy = bookedBy;
        Reason = reason.Trim();
        Status = AppointmentStatus.Requested;
    }

    public Guid SlotId { get; private set; }

    public Guid PatientId { get; private set; }

    /// <summary>The patient, a representative, or staff.</summary>
    public Guid BookedBy { get; private set; }

    /// <summary>Chief complaint, in the booker's words.</summary>
    public string Reason { get; private set; } = "";

    public AppointmentStatus Status { get; private set; }

    /// <summary>Who confirmed or declined the request, and when.</summary>
    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Shown to the patient's side: why it was declined, or a note with the confirmation.</summary>
    public string? DecisionNote { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>The physician (or front desk) accepts a request. Only before the visit.</summary>
    public Result Confirm(Guid by, DateTimeOffset at, DateTimeOffset startsAt, string? note)
    {
        if (Status != AppointmentStatus.Requested)
        {
            return Result.Failure(SchedulingErrors.AppointmentNotRequested);
        }

        if (startsAt <= at)
        {
            return Result.Failure(SchedulingErrors.AppointmentAlreadyStarted);
        }

        Status = AppointmentStatus.Confirmed;
        Decide(by, at, note);
        return Result.Success();
    }

    /// <summary>Turns a request down, which frees the slot. The reason is shown to the patient's side.</summary>
    public Result Decline(Guid by, DateTimeOffset at, string reason)
    {
        if (Status != AppointmentStatus.Requested)
        {
            return Result.Failure(SchedulingErrors.AppointmentNotRequested);
        }

        Status = AppointmentStatus.Declined;
        Decide(by, at, reason);
        return Result.Success();
    }

    /// <summary>Called off before the visit, by the patient's side, the physician or the front desk. Frees the slot.</summary>
    public Result Cancel(Guid by, DateTimeOffset at, DateTimeOffset startsAt, string reason)
    {
        if (Status is not (AppointmentStatus.Requested or AppointmentStatus.Confirmed))
        {
            return Result.Failure(SchedulingErrors.AppointmentNotCancellable);
        }

        if (startsAt <= at)
        {
            return Result.Failure(SchedulingErrors.AppointmentAlreadyStarted);
        }

        Status = AppointmentStatus.Cancelled;
        CancelledBy = by;
        CancelledAt = at;
        CancelReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>The patient didn't come to a confirmed visit. Only once its time has started.</summary>
    public Result MarkNoShow(DateTimeOffset at, DateTimeOffset startsAt)
    {
        if (Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure(SchedulingErrors.AppointmentNotConfirmed);
        }

        if (startsAt > at)
        {
            return Result.Failure(SchedulingErrors.AppointmentNotStarted);
        }

        Status = AppointmentStatus.NoShow;
        return Result.Success();
    }

    private void Decide(Guid by, DateTimeOffset at, string? note)
    {
        DecidedBy = by;
        DecidedAt = at;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

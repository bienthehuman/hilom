using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;

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
}

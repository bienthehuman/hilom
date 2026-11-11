using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

public enum EncounterType
{
    /// <summary>A clinic visit, opened by checking in for an appointment.</summary>
    Outpatient,

    /// <summary>A ward stay, with an admission (slice 3.4).</summary>
    Inpatient,
}

public enum EncounterStatus
{
    Open,
    Closed,

    /// <summary>Opened by mistake. Not built yet; kept so the column's allowed values match the data model.</summary>
    Voided,
}

/// <summary>
/// One episode of care: a clinic visit or a ward stay (docs/adr/0004-encounter-as-clinical-spine.md). Vitals,
/// notes, orders and medication administration all hang off it, and the attending physician drives the
/// physician's <c>own</c> scope. Who opened it, and when, is <see cref="Entity.CreatedBy"/>.
/// </summary>
public sealed class Encounter : Entity, IPatientRecord
{
    public const int ChiefComplaintMaxLength = 500;
    public const int DiagnosisSummaryMaxLength = 2000;

    private Encounter()
    {
    }

    private Encounter(Guid patientId, EncounterType type, Guid attendingPhysicianId, string chiefComplaint, DateTimeOffset startedAt)
    {
        PatientId = patientId;
        Type = type;
        AttendingPhysicianId = attendingPhysicianId;
        ChiefComplaint = chiefComplaint.Trim();
        Status = EncounterStatus.Open;
        StartedAt = startedAt;
    }

    public Guid PatientId { get; private set; }

    public EncounterType Type { get; private set; }

    /// <summary>The appointment this visit was checked in from. At most one encounter per appointment.</summary>
    public Guid? AppointmentId { get; private set; }

    /// <summary>For a ward stay: the clinic visit it was admitted from (slice 3.4).</summary>
    public Guid? ParentEncounterId { get; private set; }

    /// <summary>The attending physician's employee id (their physician profile).</summary>
    public Guid AttendingPhysicianId { get; private set; }

    /// <summary>Why the patient came, starting from the booking reason. Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string ChiefComplaint { get; private set; } = "";

    /// <summary>Free text; structured coding is out of scope. Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string? DiagnosisSummary { get; private set; }

    public EncounterStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Checking in for an appointment opens an outpatient visit with the appointment's physician.</summary>
    public static Encounter CheckIn(Guid patientId, Guid appointmentId, Guid physicianId, string reason, DateTimeOffset at) =>
        new(patientId, EncounterType.Outpatient, physicianId, reason, at) { AppointmentId = appointmentId };

    /// <summary>
    /// Admitting opens a ward stay, optionally from a clinic visit. The admission reason starts the chief
    /// complaint. Use <see cref="Admission.Admit"/>, which creates the admission and first bed with it.
    /// </summary>
    internal static Encounter Admit(Guid patientId, Guid? parentEncounterId, Guid physicianId, string reason, DateTimeOffset at) =>
        new(patientId, EncounterType.Inpatient, physicianId, reason, at) { ParentEncounterId = parentEncounterId };

    /// <summary>Changes the chief complaint and diagnosis summary while open. Returns the fields that changed.</summary>
    public Result<IReadOnlyList<string>> UpdateSummary(string chiefComplaint, string? diagnosisSummary)
    {
        if (Status != EncounterStatus.Open)
        {
            return Result.Failure<IReadOnlyList<string>>(ClinicalErrors.EncounterNotOpen);
        }

        var complaint = chiefComplaint.Trim();
        var diagnosis = string.IsNullOrWhiteSpace(diagnosisSummary) ? null : diagnosisSummary.Trim();
        var changed = new List<string>();
        if (complaint != ChiefComplaint)
        {
            ChiefComplaint = complaint;
            changed.Add("chiefComplaint");
        }

        if (diagnosis != DiagnosisSummary)
        {
            DiagnosisSummary = diagnosis;
            changed.Add("diagnosisSummary");
        }

        return Result.Success<IReadOnlyList<string>>(changed);
    }

    /// <summary>Ends the encounter. Nothing more is recorded on it afterwards.</summary>
    public Result Close(DateTimeOffset at)
    {
        if (Status != EncounterStatus.Open)
        {
            return Result.Failure(ClinicalErrors.EncounterNotOpen);
        }

        Status = EncounterStatus.Closed;
        EndedAt = at < StartedAt ? StartedAt : at;
        return Result.Success();
    }
}

using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using NpgsqlTypes;

namespace Hilom.Clinical.Domain;

/// <summary>How a stay ended (slice 3.6).</summary>
public enum DischargeDisposition
{
    Home,
    TransferredOut,
    AgainstAdvice,
    Expired,
}

/// <summary>
/// A ward stay: one per inpatient encounter, sharing its id (docs/DATA_MODEL.md). A patient can't be admitted
/// twice at once; the database enforces it on <see cref="PatientId"/>, copied here for that reason.
/// Where the patient is lies in <see cref="BedAssignment"/>s.
/// </summary>
public sealed class Admission : Entity, IPatientRecord
{
    public const int AdmittingDiagnosisMaxLength = 1000;
    /// <summary>The reason also starts the encounter's chief complaint, so it has the same limit.</summary>
    public const int ReasonMaxLength = Encounter.ChiefComplaintMaxLength;

    private Admission()
    {
    }

    private Admission(Encounter encounter, string admittingDiagnosis, string reason)
    {
        Id = encounter.Id;
        PatientId = encounter.PatientId;
        AdmittedAt = encounter.StartedAt;
        AdmittingDiagnosis = admittingDiagnosis.Trim();
        Reason = reason.Trim();
    }

    /// <summary>The inpatient encounter's id (the table's primary key, <c>encounter_id</c>).</summary>
    public Guid EncounterId => Id;

    public Guid PatientId { get; private set; }

    public DateTimeOffset AdmittedAt { get; private set; }

    /// <summary>Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string AdmittingDiagnosis { get; private set; } = "";

    /// <summary>Why the patient is admitted. Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string Reason { get; private set; } = "";

    public DateTimeOffset? DischargedAt { get; private set; }

    public DischargeDisposition? DischargeDisposition { get; private set; }

    public Guid? DischargedBy { get; private set; }

    /// <summary>
    /// Admits a patient: an inpatient encounter, its admission, and the first bed. The caller saves all three
    /// together, so either the patient is in the bed or nothing was recorded.
    /// </summary>
    public static (Encounter Encounter, Admission Admission, BedAssignment Bed) Admit(
        AdmitPatient admit,
        Guid wardId,
        Guid movedBy,
        DateTimeOffset at)
    {
        var encounter = Encounter.Admit(admit.PatientId, admit.ParentEncounterId, admit.AttendingPhysicianId, admit.Reason, at);
        var admission = new Admission(encounter, admit.AdmittingDiagnosis, admit.Reason);
        var bed = BedAssignment.First(admission, admit.BedId, wardId, movedBy);
        return (encounter, admission, bed);
    }
}

/// <summary>What the admitting user chose.</summary>
public sealed record AdmitPatient(
    Guid PatientId,
    Guid? ParentEncounterId,
    Guid AttendingPhysicianId,
    Guid BedId,
    string AdmittingDiagnosis,
    string Reason);

/// <summary>
/// Where an admitted patient is, and since when. A transfer closes the current row and opens a new one
/// (slice 3.6), so the rows are the stay's bed history. The database keeps one patient per bed and one bed
/// per patient at any moment.
/// </summary>
public sealed class BedAssignment : Entity
{
    public const string AdmissionReason = "Admission";

    private BedAssignment()
    {
    }

    private BedAssignment(Guid admissionId, Guid bedId, Guid wardId, DateTimeOffset from, string reason, Guid movedBy)
    {
        AdmissionId = admissionId;
        BedId = bedId;
        WardId = wardId;
        During = new NpgsqlRange<DateTime>(from.UtcDateTime, true, false, default, false, true);
        Reason = reason;
        MovedBy = movedBy;
    }

    public Guid AdmissionId { get; private set; }

    public Guid BedId { get; private set; }

    /// <summary>The bed's ward, copied for ward-scope queries. The database checks it matches the bed.</summary>
    public Guid WardId { get; private set; }

    /// <summary>When the patient was in the bed. No upper bound while they still are.</summary>
    public NpgsqlRange<DateTime> During { get; private set; }

    /// <summary>"Admission" for the first bed; the transfer reason after that.</summary>
    public string Reason { get; private set; } = "";

    /// <summary>The user who put the patient in this bed.</summary>
    public Guid MovedBy { get; private set; }

    public static BedAssignment First(Admission admission, Guid bedId, Guid wardId, Guid movedBy) =>
        new(admission.Id, bedId, wardId, admission.AdmittedAt, AdmissionReason, movedBy);
}

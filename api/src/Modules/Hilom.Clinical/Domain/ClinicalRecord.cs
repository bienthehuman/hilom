using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

/// <summary>
/// Base for entries on an encounter that are never deleted: vital signs, notes and (3.8) medication
/// administrations. A mistake is voided with a reason (docs/DATA_MODEL.md, conventions): the entry stays,
/// struck through, and nothing more changes on it.
/// </summary>
public abstract class ClinicalRecord : Entity, IPatientRecord
{
    public const int VoidReasonMaxLength = 500;

    protected ClinicalRecord()
    {
    }

    protected ClinicalRecord(Guid encounterId, Guid patientId)
    {
        EncounterId = encounterId;
        PatientId = patientId;
    }

    public Guid EncounterId { get; private set; }

    /// <summary>Copied from the encounter, so audit entries and the patient's history find it directly.</summary>
    public Guid PatientId { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>The user who voided it. A user, not an employee, so an administrator without an employee record can void.</summary>
    public Guid? VoidedBy { get; private set; }

    public string? VoidReason { get; private set; }

    public bool IsVoided => VoidedAt is not null;

    /// <summary>The employee who entered it; <c>own</c> on <c>clinical.records.void</c> means this is you.</summary>
    public abstract Guid AuthorId { get; }

    /// <summary>Marks it entered in error. A voided entry can't be voided again.</summary>
    public Result Void(Guid? by, string reason, DateTimeOffset at)
    {
        if (IsVoided)
        {
            return Result.Failure(ClinicalErrors.RecordAlreadyVoided);
        }

        VoidedAt = at;
        VoidedBy = by;
        VoidReason = reason.Trim();
        return Result.Success();
    }
}

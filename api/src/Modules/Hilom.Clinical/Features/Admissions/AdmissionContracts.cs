using FluentValidation;
using Hilom.Clinical.Domain;
using Hilom.Facility.Domain;

namespace Hilom.Clinical.Features.Admissions;

/// <param name="ParentEncounterId">The clinic visit the patient is admitted from, if any.</param>
/// <param name="AttendingPhysicianId">The physician's employee id; they become the stay's attending physician.</param>
/// <param name="BedId">A free bed, from the free-beds list.</param>
public sealed record AdmitRequest(
    Guid PatientId,
    Guid? ParentEncounterId,
    Guid AttendingPhysicianId,
    Guid BedId,
    string AdmittingDiagnosis,
    string Reason);

/// <param name="EncounterId">The new inpatient encounter (the admission shares its id).</param>
public sealed record AdmitResponse(Guid EncounterId, Guid WardId, Guid BedId);

/// <summary>A ward with its free beds, for the bed picker.</summary>
public sealed record FreeBedWardResponse(Guid Id, string Code, string Name, WardType WardType, IReadOnlyList<FreeBedResponse> Beds);

public sealed record FreeBedResponse(Guid Id, string Label);

/// <summary>A physician who can be the attending physician of a stay.</summary>
/// <param name="Id">Their employee id.</param>
public sealed record AttendingPhysicianResponse(Guid Id, string FirstName, string LastName, string Specialization);

/// <summary>Where an inpatient encounter's patient is now, or was last, and why they were admitted.</summary>
/// <param name="Ward">The ward of the current bed (the last one after discharge).</param>
/// <param name="Beds">Every bed of the stay, first to last: the admission, then each transfer.</param>
/// <param name="Version">Send back when transferring or discharging; a mismatch means the stay changed meanwhile.</param>
public sealed record AdmissionResponse(
    DateTimeOffset AdmittedAt,
    string AdmittingDiagnosis,
    string Reason,
    DateTimeOffset? DischargedAt,
    DischargeDisposition? DischargeDisposition,
    AdmissionWardRef Ward,
    AdmissionBedRef Bed,
    IReadOnlyList<BedMoveResponse> Beds,
    uint Version);

/// <summary>One bed of a stay.</summary>
/// <param name="Until">When the patient left it; null while they're still in it.</param>
/// <param name="Reason">"Admission" for the first bed, the transfer reason after that.</param>
public sealed record BedMoveResponse(
    AdmissionWardRef Ward,
    AdmissionBedRef Bed,
    DateTimeOffset From,
    DateTimeOffset? Until,
    string Reason);

/// <param name="BedId">A free bed, from the stay's free-beds list.</param>
/// <param name="Version">The stay's version (<c>admission.version</c> on the encounter).</param>
public sealed record TransferRequest(Guid BedId, string Reason, uint Version);

/// <summary>Where the patient is now.</summary>
/// <param name="CanStillView">
/// Whether the caller can still open the stay. A ward nurse who moves a patient off their wards loses it.
/// </param>
public sealed record TransferResponse(Guid EncounterId, AdmissionWardRef Ward, AdmissionBedRef Bed, bool CanStillView);

/// <param name="Summary">The discharge summary, saved as a discharge-summary note on the stay.</param>
/// <param name="Version">The stay's version (<c>admission.version</c> on the encounter).</param>
public sealed record DischargeRequest(DischargeDisposition Disposition, string Summary, uint Version);

public sealed record AdmissionWardRef(Guid Id, string Code, string Name);

public sealed record AdmissionBedRef(Guid Id, string Label);

internal sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(r => r.BedId).NotEmpty().WithMessage("Choose a free bed.");
        RuleFor(r => r.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Say why the patient is moving.")
            .MaximumLength(Admission.ReasonMaxLength);
    }
}

internal sealed class DischargeRequestValidator : AbstractValidator<DischargeRequest>
{
    public DischargeRequestValidator()
    {
        RuleFor(r => r.Disposition).IsInEnum().WithMessage("Choose how the stay ended.");
        RuleFor(r => r.Summary)
            .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("Write the discharge summary.")
            .MaximumLength(ClinicalNote.BodyMaxLength);
    }
}

internal sealed class AdmitRequestValidator : AbstractValidator<AdmitRequest>
{
    public AdmitRequestValidator()
    {
        RuleFor(r => r.PatientId).NotEmpty().WithMessage("Choose the patient.");
        RuleFor(r => r.AttendingPhysicianId).NotEmpty().WithMessage("Choose the attending physician.");
        RuleFor(r => r.BedId).NotEmpty().WithMessage("Choose a free bed.");
        RuleFor(r => r.AdmittingDiagnosis)
            .NotEmpty().WithMessage("Write the admitting diagnosis.")
            .MaximumLength(Admission.AdmittingDiagnosisMaxLength);
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("Say why the patient is admitted.")
            .MaximumLength(Admission.ReasonMaxLength);
    }
}

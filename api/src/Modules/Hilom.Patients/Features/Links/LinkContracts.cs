using FluentValidation;
using Hilom.Infrastructure.Configuration;
using Hilom.Patients.Domain;

namespace Hilom.Patients.Features.Links;

/// <summary>The patient a link reaches, once staff matched the request to a record.</summary>
public sealed record LinkedPatientRef(Guid Id, string Mrn, string FirstName, string? MiddleName, string LastName, DateOnly BirthDate);

/// <summary>A portal user's own link or request, as the portal shows it.</summary>
/// <param name="Patient">Set once active. The patient's name and MRN, even without <c>can_view_records</c>, so they can book.</param>
/// <param name="ReviewNote">Why a request was rejected, if staff said.</param>
public sealed record MyLinkResponse(
    Guid Id,
    LinkStatus Status,
    LinkRelationship Relationship,
    string RequestedPatientName,
    DateOnly RequestedBirthDate,
    DateTimeOffset RequestedAt,
    LinkedPatientRef? Patient,
    bool CanBook,
    bool CanViewRecords,
    string? ReviewNote);

public sealed record LinkRequesterRef(Guid Id, string DisplayName, string Email);

/// <summary>A link or request, as staff review and manage it.</summary>
/// <param name="Version">Send back when acting on it; a mismatch means someone else acted meanwhile.</param>
public sealed record LinkResponse(
    Guid Id,
    LinkStatus Status,
    LinkRelationship Relationship,
    LinkRequesterRef Requester,
    string RequestedPatientName,
    DateOnly RequestedBirthDate,
    string? RequestedMrn,
    string? RequestNote,
    DateTimeOffset RequestedAt,
    LinkedPatientRef? Patient,
    bool CanBook,
    bool CanViewRecords,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    string? RevokedBy,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    uint Version);

/// <param name="PatientMrn">From the patient's hospital card, if the requester has it.</param>
public sealed record RequestLinkRequest(
    string PatientName,
    DateOnly? PatientBirthDate,
    string? PatientMrn,
    LinkRelationship? Relationship,
    string? Note);

/// <param name="PatientId">The patient record that matches the request, chosen by staff.</param>
/// <param name="Relationship">As confirmed by staff; may differ from what was requested.</param>
public sealed record VerifyLinkRequest(
    Guid PatientId,
    LinkRelationship? Relationship,
    bool CanBook,
    bool CanViewRecords,
    string? Note,
    uint Version);

/// <param name="Reason">Shown to the requester.</param>
public sealed record RejectLinkRequest(string? Reason, uint Version);

public sealed record SetLinkPermissionsRequest(bool CanBook, bool CanViewRecords, uint Version);

/// <param name="Reason">Kept in the record and the audit log; not shown to the account holder.</param>
public sealed record RevokeLinkRequest(string Reason, uint Version);

public enum LinkStatusFilter
{
    Pending,
    Active,
    Rejected,
    Revoked,
}

internal sealed class RequestLinkRequestValidator : AbstractValidator<RequestLinkRequest>
{
    public RequestLinkRequestValidator(HospitalClock clock)
    {
        RuleFor(r => r.PatientName)
            .NotEmpty().WithMessage("Enter the patient's full name.")
            .MaximumLength(PatientAccountLink.NameMaxLength);
        RuleFor(r => r.PatientBirthDate)
            .NotNull().WithMessage("Enter the patient's date of birth.")
            .Must(date => date <= clock.Today).WithMessage("The date of birth can't be in the future.")
            .Must(date => date >= new DateOnly(1900, 1, 1)).WithMessage("Check the year.");
        RuleFor(r => r.PatientMrn).MaximumLength(PatientAccountLink.MrnMaxLength);
        RuleFor(r => r.Relationship).NotNull().WithMessage("Choose who the patient is to you.");
        RuleFor(r => r.Note).MaximumLength(PatientAccountLink.NoteMaxLength);
    }
}

internal sealed class VerifyLinkRequestValidator : AbstractValidator<VerifyLinkRequest>
{
    public VerifyLinkRequestValidator()
    {
        RuleFor(r => r.PatientId).NotEmpty().WithMessage("Choose the patient record that matches.");
        RuleFor(r => r.Relationship).NotNull().WithMessage("Choose the relationship.");
        RuleFor(r => r.Note).MaximumLength(PatientAccountLink.NoteMaxLength);
    }
}

internal sealed class RejectLinkRequestValidator : AbstractValidator<RejectLinkRequest>
{
    public RejectLinkRequestValidator() => RuleFor(r => r.Reason).MaximumLength(PatientAccountLink.NoteMaxLength);
}

internal sealed class RevokeLinkRequestValidator : AbstractValidator<RevokeLinkRequest>
{
    public RevokeLinkRequestValidator() =>
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("Say why the link is ending.")
            .MaximumLength(PatientAccountLink.NoteMaxLength);
}

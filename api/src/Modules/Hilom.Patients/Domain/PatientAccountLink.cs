using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Patients.Domain;

/// <summary>What the account holder is to the patient: a daughter linking her father is <see cref="Child"/>.</summary>
public enum LinkRelationship
{
    Self,
    Parent,
    Guardian,
    Spouse,
    Child,
    Representative,
}

public enum LinkStatus
{
    /// <summary>Requested by the portal user; staff haven't reviewed it yet.</summary>
    Pending,

    /// <summary>Verified by staff: the account reaches the patient, within <c>can_book</c> / <c>can_view_records</c>.</summary>
    Active,

    /// <summary>Staff couldn't verify it (e.g. no matching patient, or ID didn't check out).</summary>
    Rejected,

    /// <summary>Was active, then ended by staff. Access ends on the next request.</summary>
    Revoked,
}

/// <summary>Who the portal user says the patient is. Staff match it to a record when verifying.</summary>
public sealed record LinkRequestDetails(
    string PatientName,
    DateOnly PatientBirthDate,
    string? PatientMrn,
    LinkRelationship Relationship,
    string? Note);

/// <summary>
/// Connects a portal account to a patient (docs/adr/0003-patient-account-links.md). Many-to-many, and only
/// active after staff verify it, normally after checking ID at the front desk. A request names the patient as
/// the requester typed them; the patient record is chosen by staff when verifying, so requesting reveals
/// nothing about which patients exist.
/// </summary>
public sealed class PatientAccountLink : Entity
{
    public const int NameMaxLength = 200;
    public const int MrnMaxLength = 20;
    public const int NoteMaxLength = 500;

    /// <summary>A portal account can have this many requests waiting at once.</summary>
    public const int MaxPendingPerUser = 5;

    private PatientAccountLink()
    {
    }

    public PatientAccountLink(Guid userId, LinkRequestDetails details, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(details);
        UserId = userId;
        RequestedPatientName = details.PatientName.Trim();
        RequestedBirthDate = details.PatientBirthDate;
        RequestedMrn = Clean(details.PatientMrn)?.ToUpperInvariant();
        Relationship = details.Relationship;
        RequestNote = Clean(details.Note);
        RequestedAt = requestedAt;
        Status = LinkStatus.Pending;
    }

    /// <summary>The portal account.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Set by staff when verifying. Empty while pending, and for requests rejected before matching.</summary>
    public Guid? PatientId { get; private set; }

    public string RequestedPatientName { get; private set; } = "";

    public DateOnly RequestedBirthDate { get; private set; }

    /// <summary>From the patient's hospital card, if the requester has it.</summary>
    public string? RequestedMrn { get; private set; }

    /// <summary>As requested, then as confirmed by staff.</summary>
    public LinkRelationship Relationship { get; private set; }

    public string? RequestNote { get; private set; }

    public bool CanBook { get; private set; }

    public bool CanViewRecords { get; private set; }

    public LinkStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Who verified or rejected the request, and when.</summary>
    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Why it was rejected (shown to the requester), or a note on the verification.</summary>
    public string? ReviewNote { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokeReason { get; private set; }

    /// <summary>Staff checked who the requester is and matched the patient record.</summary>
    public Result Verify(
        Guid patientId,
        LinkRelationship relationship,
        bool canBook,
        bool canViewRecords,
        string? note,
        Guid reviewedBy,
        DateTimeOffset at)
    {
        if (Status != LinkStatus.Pending)
        {
            return Result.Failure(PatientErrors.LinkNotPending);
        }

        PatientId = patientId;
        Relationship = relationship;
        CanBook = canBook;
        CanViewRecords = canViewRecords;
        Status = LinkStatus.Active;
        Review(note, reviewedBy, at);
        return Result.Success();
    }

    public Result Reject(string? reason, Guid reviewedBy, DateTimeOffset at)
    {
        if (Status != LinkStatus.Pending)
        {
            return Result.Failure(PatientErrors.LinkNotPending);
        }

        Status = LinkStatus.Rejected;
        Review(reason, reviewedBy, at);
        return Result.Success();
    }

    /// <summary>Changes what the account may do for the patient. Returns whether anything changed.</summary>
    public Result<bool> SetPermissions(bool canBook, bool canViewRecords)
    {
        if (Status != LinkStatus.Active)
        {
            return Result.Failure<bool>(PatientErrors.LinkNotActive);
        }

        var changed = CanBook != canBook || CanViewRecords != canViewRecords;
        CanBook = canBook;
        CanViewRecords = canViewRecords;
        return Result.Success(changed);
    }

    public Result Revoke(string reason, Guid revokedBy, DateTimeOffset at)
    {
        if (Status != LinkStatus.Active)
        {
            return Result.Failure(PatientErrors.LinkNotActive);
        }

        Status = LinkStatus.Revoked;
        RevokeReason = reason.Trim();
        RevokedBy = revokedBy;
        RevokedAt = at;
        return Result.Success();
    }

    private void Review(string? note, Guid reviewedBy, DateTimeOffset at)
    {
        ReviewNote = Clean(note);
        ReviewedBy = reviewedBy;
        ReviewedAt = at;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

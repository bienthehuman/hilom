using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

public enum NoteType
{
    Assessment,
    Progress,
    Nursing,
    Diet,

    /// <summary>Written as part of discharging a patient (slice 3.6), not through the notes form.</summary>
    DischargeSummary,
}

/// <summary>
/// A clinical note on an encounter (docs/DATA_MODEL.md, clinical_notes). Its author can edit it for 24 hours;
/// after that a correction is a new note that replaces it, and the old one is voided.
/// </summary>
public sealed class ClinicalNote : ClinicalRecord
{
    public const int BodyMaxLength = 10_000;

    /// <summary>How long the author may edit a note in place.</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromHours(24);

    private ClinicalNote()
    {
    }

    public ClinicalNote(Guid encounterId, Guid patientId, NoteType type, string body, Guid authoredBy, DateTimeOffset authoredAt, Guid? replacesNoteId = null)
        : base(encounterId, patientId)
    {
        NoteType = type;
        Body = body.Trim();
        AuthoredBy = authoredBy;
        AuthoredAt = authoredAt;
        ReplacesNoteId = replacesNoteId;
    }

    public NoteType NoteType { get; private set; }

    /// <summary>Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string Body { get; private set; } = "";

    /// <summary>The employee who wrote it.</summary>
    public Guid AuthoredBy { get; private set; }

    public DateTimeOffset AuthoredAt { get; private set; }

    /// <summary>When the author last edited it within the edit window; null if never.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>The voided note this one corrects, when written as a replacement.</summary>
    public Guid? ReplacesNoteId { get; private set; }

    public override Guid AuthorId => AuthoredBy;

    /// <summary>Whether the author may still edit it in place at <paramref name="now"/>.</summary>
    public bool IsEditable(DateTimeOffset now) => !IsVoided && now < AuthoredAt + EditWindow;

    /// <summary>The author changes the type or text within 24 hours. Returns the fields that changed.</summary>
    public Result<IReadOnlyList<string>> Edit(NoteType type, string body, DateTimeOffset now)
    {
        if (IsVoided)
        {
            return Result.Failure<IReadOnlyList<string>>(ClinicalErrors.RecordAlreadyVoided);
        }

        if (!IsEditable(now))
        {
            return Result.Failure<IReadOnlyList<string>>(ClinicalErrors.NoteEditWindowClosed);
        }

        var text = body.Trim();
        var changed = new List<string>();
        if (type != NoteType)
        {
            NoteType = type;
            changed.Add("noteType");
        }

        if (text != Body)
        {
            Body = text;
            changed.Add("body");
        }

        if (changed.Count > 0)
        {
            EditedAt = now;
        }

        return Result.Success<IReadOnlyList<string>>(changed);
    }
}

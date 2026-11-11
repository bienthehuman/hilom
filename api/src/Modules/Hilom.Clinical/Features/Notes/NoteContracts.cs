using FluentValidation;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Voiding;

namespace Hilom.Clinical.Features.Notes;

/// <param name="AuthoredBy">The employee's name.</param>
/// <param name="EditedAt">When the author last edited it, if they did.</param>
/// <param name="EditableUntil">The end of the author's 24-hour edit window.</param>
/// <param name="ReplacesNoteId">The voided note this one corrects.</param>
/// <param name="ReplacedByNoteId">The correction that replaced this note, if any.</param>
/// <param name="Voided">Set when it was voided; it stays in the list, struck through.</param>
/// <param name="CanEdit">The caller wrote it, may write notes on this encounter, and it's within 24 hours.</param>
/// <param name="CanCorrect">The caller may write a correction that replaces it (writing notes here, and voiding it).</param>
/// <param name="CanVoid">The caller may void it (<c>clinical.records.void</c>: all, or own = they wrote it).</param>
/// <param name="Version">Send back when editing; a mismatch means it changed meanwhile.</param>
public sealed record ClinicalNoteResponse(
    Guid Id,
    NoteType NoteType,
    string Body,
    DateTimeOffset AuthoredAt,
    string AuthoredBy,
    DateTimeOffset? EditedAt,
    DateTimeOffset EditableUntil,
    Guid? ReplacesNoteId,
    Guid? ReplacedByNoteId,
    VoidedResponse? Voided,
    bool CanEdit,
    bool CanCorrect,
    bool CanVoid,
    uint Version);

public sealed record WriteNoteRequest(NoteType NoteType, string Body);

public sealed record EditNoteRequest(NoteType NoteType, string Body, uint Version);

/// <summary>A correction: a new note in place of this one, which is voided with <paramref name="Reason"/>.</summary>
public sealed record CorrectNoteRequest(NoteType NoteType, string Body, string Reason);

/// <summary>Note rules shared by writing, editing and correcting.</summary>
internal static class NoteRules
{
    public const string DischargeSummaryMessage = "Discharge summaries are written when discharging the patient.";

    /// <param name="allowDischargeSummary">Editing keeps a discharge summary's type; the handler checks it isn't changed to or from one.</param>
    public static void TypeAndBody<T>(AbstractValidator<T> validator, Func<T, NoteType> type, Func<T, string> body, bool allowDischargeSummary = false)
    {
        validator.RuleFor(r => type(r))
            .IsInEnum().WithMessage("Choose the kind of note.")
            .Must(t => allowDischargeSummary || t != NoteType.DischargeSummary).WithMessage(DischargeSummaryMessage)
            .OverridePropertyName("noteType");
        validator.RuleFor(r => body(r))
            .Must(b => !string.IsNullOrWhiteSpace(b)).WithMessage("Write the note.")
            .MaximumLength(ClinicalNote.BodyMaxLength)
            .OverridePropertyName("body");
    }
}

internal sealed class WriteNoteRequestValidator : AbstractValidator<WriteNoteRequest>
{
    public WriteNoteRequestValidator() => NoteRules.TypeAndBody(this, r => r.NoteType, r => r.Body);
}

internal sealed class EditNoteRequestValidator : AbstractValidator<EditNoteRequest>
{
    public EditNoteRequestValidator() => NoteRules.TypeAndBody(this, r => r.NoteType, r => r.Body, allowDischargeSummary: true);
}

internal sealed class CorrectNoteRequestValidator : AbstractValidator<CorrectNoteRequest>
{
    public CorrectNoteRequestValidator()
    {
        NoteRules.TypeAndBody(this, r => r.NoteType, r => r.Body);
        RuleFor(r => r.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Say what was wrong with the original note.")
            .MaximumLength(ClinicalRecord.VoidReasonMaxLength);
    }
}

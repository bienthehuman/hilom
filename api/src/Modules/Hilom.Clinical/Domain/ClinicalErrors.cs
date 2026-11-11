using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

/// <summary>Expected failures in the Clinical module, with stable codes the web app relies on.</summary>
public static class ClinicalErrors
{
    public static readonly Error EncounterNotFound = Error.NotFound("encounter_not_found", "This encounter doesn't exist.");

    public static readonly Error EncounterNotOpen = Error.Conflict(
        "encounter_not_open",
        "This encounter is already closed.");

    public static readonly Error NoEmployeeRecord = Error.BusinessRule(
        "no_employee_record",
        "Only staff with an employee record can record this. Ask HR to link your account to your employee record.");

    public static readonly Error RecordNotFound = Error.NotFound("record_not_found", "This entry doesn't exist on this encounter.");

    public static readonly Error RecordAlreadyVoided = Error.Conflict(
        "record_already_voided",
        "This entry has already been voided.");

    public static readonly Error NoteNotFound = Error.NotFound("note_not_found", "This note doesn't exist on this encounter.");

    public static readonly Error NoteEditWindowClosed = Error.BusinessRule(
        "note_edit_window_closed",
        "Notes can only be edited for 24 hours. Write a correction instead; it replaces this note, which is voided.");

    public static readonly Error NotNoteAuthor = Error.Forbidden(
        "not_note_author",
        "Only the person who wrote a note can edit it.");

    public static Error InvalidRecordedAt(string message) =>
        Error.Validation("invalid_recorded_at", message, new Dictionary<string, string[]> { ["recordedAt"] = [message] });
}

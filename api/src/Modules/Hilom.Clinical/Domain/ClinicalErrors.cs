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

    public static readonly Error PatientNotFound = Error.NotFound("patient_not_found", "This patient doesn't exist.");

    public static readonly Error AlreadyAdmitted = Error.Conflict(
        "patient_already_admitted",
        "This patient is already admitted. Find their current stay on the ward board or their record.");

    public static readonly Error BedTaken = Error.Conflict(
        "bed_taken",
        "Someone else was just put in this bed. Choose another free bed.");

    public static readonly Error BedNotAvailable = Error.BusinessRule(
        "bed_not_available",
        "This bed is out of service or its ward is inactive. Choose another free bed.");

    public static readonly Error PhysicianNotAvailable = Error.BusinessRule(
        "physician_not_available",
        "The attending physician must be a current physician on staff.");

    public static readonly Error InvalidParentEncounter = Error.BusinessRule(
        "invalid_parent_encounter",
        "A patient can only be admitted from one of their own clinic visits.");

    public static readonly Error AdmissionNotFound = Error.NotFound("admission_not_found", "This ward stay doesn't exist.");

    public static readonly Error AlreadyDischarged = Error.Conflict(
        "patient_already_discharged",
        "This patient has already been discharged from this stay.");

    public static readonly Error SameBed = Error.BusinessRule(
        "same_bed",
        "The patient is already in this bed. Choose another free bed.");

    public static readonly Error DischargeToClose = Error.BusinessRule(
        "discharge_to_close",
        "A ward stay ends by discharging the patient, which closes it.");

    public static Error InvalidRecordedAt(string message) =>
        Error.Validation("invalid_recorded_at", message, new Dictionary<string, string[]> { ["recordedAt"] = [message] });
}

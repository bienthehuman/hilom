using FluentValidation;
using Hilom.Clinical.Domain;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Domain;

namespace Hilom.Clinical.Features.Encounters;

/// <summary>Enough to identify the patient in the encounter's header.</summary>
public sealed record EncounterPatientRef(Guid Id, string Mrn, string FirstName, string? MiddleName, string LastName, DateOnly BirthDate, Sex Sex);

/// <param name="Id">The physician's employee id.</param>
public sealed record EncounterPhysicianRef(Guid Id, string FirstName, string LastName, string Specialization);

/// <summary>An active allergy, for the header banner and (later) warnings before giving medication.</summary>
public sealed record EncounterAllergyRef(string Allergen, AllergySeverity Severity, string? Reaction);

/// <summary>One row in an encounter list.</summary>
public sealed record EncounterSummaryResponse(
    Guid Id,
    EncounterType Type,
    EncounterStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string ChiefComplaint,
    EncounterPatientRef Patient,
    EncounterPhysicianRef AttendingPhysician);

/// <param name="AppointmentStartsAt">When the appointment it was checked in from was booked for.</param>
/// <param name="OpenedBy">Who opened it, e.g. the front desk on check-in.</param>
/// <param name="ActiveAllergies">The patient's active allergies, most severe first.</param>
/// <param name="CanManage">Whether the caller may change the summary or close it (<c>clinical.encounters.manage</c> in scope, and still open).</param>
/// <param name="CanRecordVitals">Whether the caller may record vital signs on it now (in scope, open, and they have an employee record).</param>
/// <param name="CanWriteNotes">Whether the caller may write a new note on it now (in scope, open, and they have an employee record).</param>
/// <param name="Version">Send back when changing it; a mismatch means someone else changed it meanwhile.</param>
public sealed record EncounterResponse(
    Guid Id,
    EncounterType Type,
    EncounterStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string ChiefComplaint,
    string? DiagnosisSummary,
    EncounterPatientRef Patient,
    EncounterPhysicianRef AttendingPhysician,
    Guid? AppointmentId,
    DateTimeOffset? AppointmentStartsAt,
    Guid? ParentEncounterId,
    string? OpenedBy,
    IReadOnlyList<EncounterAllergyRef> ActiveAllergies,
    bool CanManage,
    bool CanRecordVitals,
    bool CanWriteNotes,
    uint Version);

/// <param name="Version">The appointment's version, as last seen on the day list.</param>
public sealed record CheckInRequest(Guid AppointmentId, uint Version);

public sealed record CheckInResponse(Guid EncounterId, Guid AppointmentId);

public sealed record UpdateEncounterRequest(string ChiefComplaint, string? DiagnosisSummary, uint Version);

public sealed record CloseEncounterRequest(uint Version);

internal sealed class CheckInRequestValidator : AbstractValidator<CheckInRequest>
{
    public CheckInRequestValidator() => RuleFor(r => r.AppointmentId).NotEmpty().WithMessage("Choose the appointment.");
}

internal sealed class UpdateEncounterRequestValidator : AbstractValidator<UpdateEncounterRequest>
{
    public UpdateEncounterRequestValidator()
    {
        RuleFor(r => r.ChiefComplaint)
            .NotEmpty().WithMessage("Say why the patient came.")
            .MaximumLength(Encounter.ChiefComplaintMaxLength);
        RuleFor(r => r.DiagnosisSummary).MaximumLength(Encounter.DiagnosisSummaryMaxLength);
    }
}

using FluentValidation;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Admissions;
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
/// <param name="ParentEncounterId">For a ward stay: the clinic visit it was admitted from.</param>
/// <param name="AdmittedAsEncounterId">For a clinic visit: the ward stay it led to, if the patient was admitted from it.</param>
/// <param name="Admission">For a ward stay: the admission and where the patient is now.</param>
/// <param name="OpenedBy">Who opened it, e.g. the front desk on check-in.</param>
/// <param name="ActiveAllergies">The patient's active allergies, most severe first.</param>
/// <param name="CanManage">Whether the caller may change the summary or close it (<c>clinical.encounters.manage</c> in scope, and still open).</param>
/// <param name="CanRecordVitals">Whether the caller may record vital signs on it now (in scope, open, and they have an employee record).</param>
/// <param name="CanWriteNotes">Whether the caller may write a new note on it now (in scope, open, and they have an employee record).</param>
/// <param name="CanAdmit">Whether the caller may admit the patient from this clinic visit (they have <c>clinical.admissions.admit</c>, it isn't voided, and the patient isn't already admitted).</param>
/// <param name="CanTransfer">Whether the caller may move the patient to another bed now (<c>clinical.admissions.transfer</c> in scope, still in a bed).</param>
/// <param name="CanDischarge">Whether the caller may discharge the patient now (<c>clinical.admissions.discharge</c> in scope, still in a bed, and they have an employee record to sign the summary).</param>
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
    Guid? AdmittedAsEncounterId,
    AdmissionResponse? Admission,
    string? OpenedBy,
    IReadOnlyList<EncounterAllergyRef> ActiveAllergies,
    bool CanManage,
    bool CanRecordVitals,
    bool CanWriteNotes,
    bool CanAdmit,
    bool CanTransfer,
    bool CanDischarge,
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

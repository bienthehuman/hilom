using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Vitals;
using Hilom.Scheduling.Domain;

namespace Hilom.Clinical.Features.PortalRecords;

/// <summary>The physician's name and specialization, as the family knows them.</summary>
public sealed record PortalPhysicianRef(string FirstName, string LastName, string Specialization);

/// <summary>
/// A visit or ward stay, in the portal-safe view: what it was for, what was found, and how it ended. No nursing
/// notes, orders or medication record (docs/RBAC.md).
/// </summary>
/// <param name="DiagnosisSummary">The physician's summary, once written.</param>
/// <param name="DischargeSummary">For a ward stay that has ended: the discharge summary.</param>
/// <param name="DischargeDisposition">For a ward stay that has ended: how the patient left.</param>
public sealed record PortalVisitResponse(
    Guid Id,
    EncounterType Type,
    EncounterStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string ChiefComplaint,
    string? DiagnosisSummary,
    string? DischargeSummary,
    DischargeDisposition? DischargeDisposition,
    PortalPhysicianRef Physician);

/// <summary>One set of vital signs. Staff notes and who took it are left out.</summary>
public sealed record PortalVitalsResponse(
    Guid Id,
    DateTimeOffset RecordedAt,
    decimal? TemperatureC,
    short? SystolicBp,
    short? DiastolicBp,
    short? HeartRate,
    short? RespiratoryRate,
    short? OxygenSaturation,
    short? PainScore,
    decimal? WeightKg,
    decimal? HeightCm,
    decimal? Bmi,
    VitalFlagsResponse Flags);

public sealed record PortalAppointmentResponse(
    Guid Id,
    AppointmentStatus Status,
    string Reason,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    PortalPhysicianRef Physician);

/// <summary>What a link with <c>can_view_records</c> may read about the patient: nothing beyond these lists.</summary>
public sealed record PortalRecordsResponse(
    Guid PatientId,
    IReadOnlyList<PortalVisitResponse> Visits,
    IReadOnlyList<PortalVitalsResponse> Vitals,
    IReadOnlyList<PortalAppointmentResponse> Appointments);

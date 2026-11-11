using Hilom.Facility.Domain;

namespace Hilom.Clinical.Features.Census;

/// <summary>A ward on the board, with every bed: occupied ones, and active ones that are free.</summary>
/// <param name="Occupied">Beds with a patient in them now.</param>
/// <param name="Beds">In natural order ("Bed 2" before "Bed 10").</param>
public sealed record CensusWardResponse(
    Guid Id,
    string Code,
    string Name,
    WardType WardType,
    int Occupied,
    IReadOnlyList<CensusBedResponse> Beds);

/// <param name="Patient">Who is in the bed now; null when it's free.</param>
public sealed record CensusBedResponse(Guid Id, string Label, CensusPatientResponse? Patient);

/// <param name="EncounterId">The stay, to open from the board.</param>
/// <param name="CanOpen">Whether the caller can open the stay (<c>clinical.encounters.view</c> reaches it).</param>
/// <param name="AdmittedHours">Whole hours since admission.</param>
/// <param name="SevereAllergies">Active severe allergens, by name.</param>
/// <param name="CriticalVitals">Measures in a critical range in the latest vital signs of the stay (voided ones skipped).</param>
/// <param name="LastVitalsAt">When the latest vital signs of the stay were taken; null when none yet.</param>
public sealed record CensusPatientResponse(
    Guid Id,
    string Mrn,
    string FirstName,
    string LastName,
    Guid EncounterId,
    bool CanOpen,
    CensusPhysicianRef AttendingPhysician,
    int AdmittedHours,
    IReadOnlyList<string> SevereAllergies,
    IReadOnlyList<string> CriticalVitals,
    DateTimeOffset? LastVitalsAt);

/// <param name="Id">The physician's employee id.</param>
public sealed record CensusPhysicianRef(Guid Id, string FirstName, string LastName);

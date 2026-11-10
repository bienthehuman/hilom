namespace Hilom.Scheduling.Visits;

/// <summary>
/// Which encounter each checked-in appointment opened, so appointment lists can link to the visit. Encounters
/// belong to Clinical, above Scheduling, so Clinical implements this (docs/ARCHITECTURE.md §3, rule 5).
/// </summary>
public interface IAppointmentEncounters
{
    /// <summary>Encounter ids by appointment id, for the appointments that have one.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> EncounterIdsAsync(IReadOnlyCollection<Guid> appointmentIds, CancellationToken cancellationToken);
}

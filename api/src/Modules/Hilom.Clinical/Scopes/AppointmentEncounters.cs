using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Scheduling.Visits;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Scopes;

/// <summary>Tells Scheduling which encounter each checked-in appointment opened.</summary>
internal sealed class AppointmentEncounters(AppDbContext db) : IAppointmentEncounters
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> EncounterIdsAsync(
        IReadOnlyCollection<Guid> appointmentIds,
        CancellationToken cancellationToken) =>
        await db.Set<Encounter>()
            .Where(e => e.AppointmentId != null && appointmentIds.Contains(e.AppointmentId.Value))
            .ToDictionaryAsync(e => e.AppointmentId!.Value, e => e.Id, cancellationToken);
}

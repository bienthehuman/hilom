using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hilom.Infrastructure.Persistence;

/// <summary>Fills CreatedAt/By and UpdatedAt/By on every save, so features never set them by hand.</summary>
public sealed class EntityTimestampsInterceptor(TimeProvider time, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context?.ChangeTracker, time.GetUtcNow(), currentUser.UserId);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context?.ChangeTracker, time.GetUtcNow(), currentUser.UserId);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    internal static void Stamp(ChangeTracker? changeTracker, DateTimeOffset now, Guid? userId)
    {
        if (changeTracker is null)
        {
            return;
        }

        foreach (var entry in changeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.MarkCreated(now, userId);
                    break;
                case EntityState.Modified:
                    entry.Entity.MarkUpdated(now, userId);
                    break;
            }
        }
    }
}

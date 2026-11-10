using System.Reflection;
using System.Text.Json;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hilom.Infrastructure.Auditing;

/// <summary>
/// Writes a <c>data_change</c> audit entry for every added, modified or deleted row in the same save, with
/// field-level before/after values (docs/AUDIT.md). Sensitive fields record that they changed, never the value.
/// Bookkeeping columns (timestamps, row version) are left out.
/// </summary>
public sealed class AuditChangesInterceptor(IHttpContextAccessor httpContextAccessor, TimeProvider time) : SaveChangesInterceptor
{
    private const string Redacted = "[redacted]";

    private static readonly HashSet<string> Bookkeeping =
    [
        nameof(Entity.CreatedAt),
        nameof(Entity.CreatedBy),
        nameof(Entity.UpdatedAt),
        nameof(Entity.UpdatedBy),
        nameof(Entity.Version),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var request = AuditRequestInfo.From(httpContextAccessor.HttpContext);
        var now = time.GetUtcNow();

        var changed = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditEntry
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in changed)
        {
            var changes = Changes(entry);
            if (changes.Count == 0)
            {
                continue;
            }

            context.Add(new AuditEntry(AuditEvents.DataChanged, AuditKind.DataChange, AuditOutcome.Success, now, request)
            {
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = EntityIdOf(entry),
                PatientId = (entry.Entity as IPatientRecord)?.PatientId,
                Changes = JsonSerializer.Serialize(changes, AuditLog.Json),
                Details = JsonSerializer.Serialize(new { operation = entry.State.ToString().ToLowerInvariant() }, AuditLog.Json),
            });
        }
    }

    private static Dictionary<string, object?[]> Changes(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?[]>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (Bookkeeping.Contains(name) || (property.Metadata.IsShadowProperty() && !property.Metadata.IsKey()))
            {
                continue;
            }

            var (before, after) = entry.State switch
            {
                EntityState.Added => (null, property.CurrentValue),
                EntityState.Deleted => (property.OriginalValue, null),
                _ => (property.OriginalValue, property.CurrentValue),
            };

            if (entry.State == EntityState.Modified && (!property.IsModified || Equals(before, after)))
            {
                continue;
            }

            if (IsSensitive(property.Metadata.PropertyInfo))
            {
                (before, after) = (before is null ? null : Redacted, after is null ? null : Redacted);
            }

            changes[JsonNamingPolicy.CamelCase.ConvertName(name)] = [before, after];
        }

        return changes;
    }

    private static bool IsSensitive(PropertyInfo? property) =>
        property?.GetCustomAttribute<SensitiveDataAttribute>() is not null;

    /// <summary>The row's id; for rows keyed by several columns (e.g. user ↔ role), the first id column.</summary>
    private static Guid? EntityIdOf(EntityEntry entry) =>
        entry.Metadata.FindPrimaryKey()?.Properties
            .Select(p => entry.Property(p.Name).CurrentValue)
            .OfType<Guid>()
            .Select(id => (Guid?)id)
            .FirstOrDefault();
}

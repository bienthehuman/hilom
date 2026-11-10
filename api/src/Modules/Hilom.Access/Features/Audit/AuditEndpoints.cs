using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Audit;

public sealed record AuditEventResponse(string Key, string Label, string Category, AuditSeverity Severity);

/// <param name="UserName">Who acted, when known.</param>
/// <param name="Changes">For data changes: <c>{ "field": [old, new] }</c>.</param>
/// <param name="Details">Event-specific facts, e.g. before/after role lists.</param>
public sealed record AuditEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditKind Kind,
    string Event,
    string Label,
    string Category,
    AuditSeverity Severity,
    AuditOutcome Outcome,
    Guid? UserId,
    string? UserName,
    string? UserEmail,
    string? AttemptedIdentifier,
    Guid? PatientId,
    string? EntityType,
    Guid? EntityId,
    JsonElement? Changes,
    JsonElement? Details,
    string CorrelationId,
    string? IpAddress,
    string? UserAgent);

/// <summary>Reading the audit log (docs/AUDIT.md, "Who can see the log"). Viewing it is itself audited.</summary>
internal static class AuditEndpoints
{
    public static Ok<List<AuditEventResponse>> ListEvents() =>
        TypedResults.Ok(AuditEvents.All.Select(e => new AuditEventResponse(e.Key, e.Label, e.Category, e.Severity)).ToList());

    public static async Task<Ok<PagedResponse<AuditEntryResponse>>> ListEntries(
        AppDbContext db,
        IAuditLog auditLog,
        int? page,
        int? pageSize,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? q,
        string? @event,
        string? category,
        AuditSeverity? severity,
        AuditOutcome? outcome,
        AuditKind? kind,
        Guid? userId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var entries = db.Set<AuditEntry>().AsNoTracking();

        if (from is not null)
        {
            entries = entries.Where(e => e.OccurredAt >= from);
        }

        if (to is not null)
        {
            entries = entries.Where(e => e.OccurredAt < to);
        }

        if (!string.IsNullOrWhiteSpace(@event))
        {
            entries = entries.Where(e => e.Event == @event);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            entries = entries.Where(e => e.Category == category);
        }

        if (severity is not null)
        {
            entries = entries.Where(e => e.Severity == severity);
        }

        if (outcome is not null)
        {
            entries = entries.Where(e => e.Outcome == outcome);
        }

        if (kind is not null)
        {
            entries = entries.Where(e => e.Kind == kind);
        }

        if (userId is not null)
        {
            entries = entries.Where(e => e.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            entries = entries.Where(e => e.CorrelationId == correlationId);
        }

        var users = db.Set<User>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            entries = entries.Where(e =>
                (e.AttemptedIdentifier != null && EF.Functions.ILike(e.AttemptedIdentifier, pattern))
                || users.Any(u => u.Id == e.UserId && (EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.DisplayName, pattern))));
        }

        var total = await entries.CountAsync(cancellationToken);
        var rows = await entries
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(e => new
            {
                Entry = e,
                User = users.Where(u => u.Id == e.UserId).Select(u => new { u.DisplayName, u.Email }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        // Opening the log is recorded (first page only, so paging doesn't flood it).
        if (pageNumber == 1)
        {
            await auditLog.RecordNowAsync(
                AuditEvents.Access.AuditViewed,
                new AuditOptions { Details = new { from, to, q, @event, category, severity, outcome, kind, userId } },
                cancellationToken);
        }

        var labels = AuditEvents.All.ToDictionary(e => e.Key, e => e.Label);
        var items = rows.Select(row => new AuditEntryResponse(
            row.Entry.Id,
            row.Entry.OccurredAt,
            row.Entry.Kind,
            row.Entry.Event,
            labels.GetValueOrDefault(row.Entry.Event, row.Entry.Event),
            row.Entry.Category,
            row.Entry.Severity,
            row.Entry.Outcome,
            row.Entry.UserId,
            row.User?.DisplayName,
            row.User?.Email,
            row.Entry.AttemptedIdentifier,
            row.Entry.PatientId,
            row.Entry.EntityType,
            row.Entry.EntityId,
            ParseJson(row.Entry.Changes),
            ParseJson(row.Entry.Details),
            row.Entry.CorrelationId,
            row.Entry.IpAddress?.ToString(),
            row.Entry.UserAgent)).ToList();

        return TypedResults.Ok(new PagedResponse<AuditEntryResponse>(items, total, pageNumber, size));
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

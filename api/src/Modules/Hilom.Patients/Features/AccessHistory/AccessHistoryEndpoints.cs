using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Patients.Features.AccessHistory;

/// <summary>One line of a patient's access history: who opened or changed their records, and when.</summary>
/// <param name="Label">The event in words, e.g. "Patient record viewed".</param>
/// <param name="EntityType">For data changes: the kind of record, e.g. <c>PatientAllergy</c>.</param>
/// <param name="Operation">For data changes: <c>added</c>, <c>modified</c> or <c>deleted</c>.</param>
/// <param name="ChangedFields">For data changes: the fields that changed, never their values.</param>
/// <param name="ViaPortal">The person acted through a patient portal link (a relative or the patient).</param>
public sealed record PatientAccessEntryResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditKind Kind,
    string Event,
    string Label,
    AuditOutcome Outcome,
    string? UserName,
    string? UserEmail,
    string? EntityType,
    string? Operation,
    IReadOnlyList<string> ChangedFields,
    bool ViaPortal);

/// <summary>
/// The patient "Access history" tab (docs/AUDIT.md, "Who can see the log"). It reads every audit entry carrying the
/// patient's id: record views, events, data changes and denied requests. Field values stay in the full audit log.
/// </summary>
internal static class AccessHistoryEndpoints
{
    public static async Task<Results<Ok<PagedResponse<PatientAccessEntryResponse>>, ProblemHttpResult>> ListAccessHistory(
        Guid id,
        AppDbContext db,
        IAuditLog auditLog,
        int? page,
        int? pageSize,
        AuditKind? kind,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<Patient>().AnyAsync(p => p.Id == id, cancellationToken))
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var entries = db.Set<AuditEntry>().AsNoTracking().Where(e => e.PatientId == id);
        if (kind is { } k)
        {
            entries = entries.Where(e => e.Kind == k);
        }

        var total = await entries.CountAsync(cancellationToken);
        var pageOfEntries = entries
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size);

        // A left join on the user's primary key, like the audit log page.
        var rows = await (
                from e in pageOfEntries
                join u in db.Set<User>().AsNoTracking() on e.UserId equals u.Id into matched
                from u in matched.DefaultIfEmpty()
                select new
                {
                    e.Id,
                    e.OccurredAt,
                    e.Kind,
                    e.Event,
                    e.Outcome,
                    e.EntityType,
                    e.Changes,
                    e.Details,
                    e.ActingForPatientLinkId,
                    e.AttemptedIdentifier,
                    User = u == null ? null : new { u.DisplayName, u.Email },
                })
            .ToListAsync(cancellationToken);

        // Reading the history is reading the audit log. It isn't tagged with the patient, so it doesn't fill the
        // history it's reading.
        if (pageNumber == 1)
        {
            await auditLog.RecordNowAsync(
                AuditEvents.Access.AuditViewed,
                new AuditOptions { Details = new { view = "patient_access_history", patientId = id, kind } },
                cancellationToken);
        }

        var labels = AuditEvents.All.ToDictionary(e => e.Key, e => e.Label);
        var items = rows.Select(r => new PatientAccessEntryResponse(
            r.Id,
            r.OccurredAt,
            r.Kind,
            r.Event,
            labels.GetValueOrDefault(r.Event, r.Event),
            r.Outcome,
            r.User?.DisplayName,
            r.User?.Email ?? r.AttemptedIdentifier,
            r.EntityType,
            r.Kind == AuditKind.DataChange ? OperationOf(r.Details) : null,
            r.Kind == AuditKind.DataChange ? FieldsOf(r.Changes) : [],
            r.ActingForPatientLinkId is not null)).ToList();

        return TypedResults.Ok(new PagedResponse<PatientAccessEntryResponse>(items, total, pageNumber, size));
    }

    private static List<string> FieldsOf(string? changes)
    {
        if (changes is null)
        {
            return [];
        }

        using var json = JsonDocument.Parse(changes);
        return json.RootElement.ValueKind == JsonValueKind.Object
            ? [.. json.RootElement.EnumerateObject().Select(p => p.Name)]
            : [];
    }

    private static string? OperationOf(string? details)
    {
        if (details is null)
        {
            return null;
        }

        using var json = JsonDocument.Parse(details);
        return json.RootElement.ValueKind == JsonValueKind.Object
               && json.RootElement.TryGetProperty("operation", out var operation)
               && operation.ValueKind == JsonValueKind.String
            ? operation.GetString()
            : null;
    }
}

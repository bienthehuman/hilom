using System.Globalization;
using System.Text;
using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Audit;

public sealed record AuditEventResponse(string Key, string Label, string Category, AuditSeverity Severity);

/// <param name="UserName">Who acted, when known.</param>
/// <param name="ActingForPatientLinkId">Set when a portal user acted through a patient account link.</param>
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
    Guid? ActingForPatientLinkId,
    string? EntityType,
    Guid? EntityId,
    JsonElement? Changes,
    JsonElement? Details,
    string CorrelationId,
    string? IpAddress,
    string? UserAgent);

/// <summary>The filters shared by the audit log list and its CSV export.</summary>
public sealed class AuditFilter
{
    [FromQuery(Name = "from")]
    public DateTimeOffset? From { get; init; }

    [FromQuery(Name = "to")]
    public DateTimeOffset? To { get; init; }

    /// <summary>Name or email of who acted, or the email typed on a failed sign-in.</summary>
    [FromQuery(Name = "q")]
    public string? Q { get; init; }

    [FromQuery(Name = "event")]
    public string? Event { get; init; }

    [FromQuery(Name = "category")]
    public string? Category { get; init; }

    [FromQuery(Name = "severity")]
    public AuditSeverity? Severity { get; init; }

    [FromQuery(Name = "outcome")]
    public AuditOutcome? Outcome { get; init; }

    [FromQuery(Name = "kind")]
    public AuditKind? Kind { get; init; }

    [FromQuery(Name = "userId")]
    public Guid? UserId { get; init; }

    /// <summary>Entries about one patient (views, changes and denied requests).</summary>
    [FromQuery(Name = "patientId")]
    public Guid? PatientId { get; init; }

    [FromQuery(Name = "correlationId")]
    public string? CorrelationId { get; init; }

    /// <summary>What was asked for, as recorded on <c>access.audit.viewed</c> and <c>access.audit.exported</c>.</summary>
    internal object Describe() => new { From, To, Q, Event, Category, Severity, Outcome, Kind, UserId, PatientId, CorrelationId };
}

/// <summary>Reading the audit log (docs/AUDIT.md, "Who can see the log"). Viewing and exporting it are themselves audited.</summary>
internal static class AuditEndpoints
{
    /// <summary>Most rows one CSV export may hold; narrow the filters for more.</summary>
    public const int MaxExportRows = 50_000;

    private const int MaxRelated = 200;

    public static Ok<List<AuditEventResponse>> ListEvents() =>
        TypedResults.Ok(AuditEvents.All.Select(e => new AuditEventResponse(e.Key, e.Label, e.Category, e.Severity)).ToList());

    public static async Task<Ok<PagedResponse<AuditEntryResponse>>> ListEntries(
        AppDbContext db,
        IAuditLog auditLog,
        [AsParameters] AuditFilter filter,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var entries = await FilteredAsync(db, filter, cancellationToken);

        var total = await entries.CountAsync(cancellationToken);
        var items = await ToResponsesAsync(
            db,
            entries.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Skip((pageNumber - 1) * size).Take(size),
            cancellationToken);

        // Opening the log is recorded (first page only, so paging doesn't flood it).
        if (pageNumber == 1)
        {
            await auditLog.RecordNowAsync(AuditEvents.Access.AuditViewed, new AuditOptions { Details = filter.Describe() }, cancellationToken);
        }

        return TypedResults.Ok(new PagedResponse<AuditEntryResponse>(items, total, pageNumber, size));
    }

    /// <summary>
    /// The other entries written by the same request (same correlation id), oldest first: a named event's data changes,
    /// or the event behind a data change. Not recorded as a view: it's part of reading an entry already listed.
    /// </summary>
    public static async Task<Results<Ok<List<AuditEntryResponse>>, ProblemHttpResult>> ListRelated(
        AppDbContext db,
        Guid id,
        CancellationToken cancellationToken)
    {
        var correlationId = await db.Set<AuditEntry>().Where(e => e.Id == id).Select(e => e.CorrelationId).SingleOrDefaultAsync(cancellationToken);
        if (correlationId is null)
        {
            return Error.NotFound("audit_entry_not_found", "No audit entry has this id.").ToProblemResult();
        }

        // Startup seeding and background work share one "system" id; those entries aren't one request.
        if (correlationId == AuditRequestInfo.System.CorrelationId)
        {
            return TypedResults.Ok(new List<AuditEntryResponse>());
        }

        var related = db.Set<AuditEntry>().AsNoTracking()
            .Where(e => e.CorrelationId == correlationId && e.Id != id)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Take(MaxRelated);
        return TypedResults.Ok(await ToResponsesAsync(db, related, cancellationToken));
    }

    /// <summary>
    /// The filtered log as CSV, newest first. Recorded as <c>access.audit.exported</c> (critical) before the file is sent,
    /// because an export takes the log out of the system. Returns <see cref="IResult"/> so the OpenAPI response comes from
    /// the endpoint's <c>Produces</c> calls (a CSV string), not from the file result type.
    /// </summary>
    public static async Task<IResult> Export(
        AppDbContext db,
        IAuditLog auditLog,
        TimeProvider time,
        [AsParameters] AuditFilter filter,
        CancellationToken cancellationToken)
    {
        var entries = await FilteredAsync(db, filter, cancellationToken);
        var total = await entries.CountAsync(cancellationToken);
        if (total > MaxExportRows)
        {
            return Error.BusinessRule(
                "export_too_large",
                string.Create(CultureInfo.InvariantCulture, $"{total:N0} entries match. An export holds at most {MaxExportRows:N0}; narrow the dates or filters."))
                .ToProblem();
        }

        var rows = await ToResponsesAsync(db, entries.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id), cancellationToken);

        await auditLog.RecordNowAsync(
            AuditEvents.Access.AuditExported,
            new AuditOptions { Details = new { filter = filter.Describe(), rows = rows.Count } },
            cancellationToken);

        var fileName = string.Create(CultureInfo.InvariantCulture, $"audit-log-{time.GetUtcNow():yyyyMMdd-HHmmss}.csv");
        return TypedResults.File(AuditCsv.Write(rows), "text/csv", fileName);
    }

    /// <summary>Re-checks the hash chain and reports the first entry that was altered, removed or inserted directly.</summary>
    public static async Task<Ok<AuditChainReport>> Verify(AuditChain chain, CancellationToken cancellationToken) =>
        TypedResults.Ok(await chain.VerifyAsync(cancellationToken));

    private static async Task<IQueryable<AuditEntry>> FilteredAsync(AppDbContext db, AuditFilter filter, CancellationToken cancellationToken)
    {
        var entries = db.Set<AuditEntry>().AsNoTracking();

        if (filter.From is { } from)
        {
            entries = entries.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            entries = entries.Where(e => e.OccurredAt < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Event))
        {
            entries = entries.Where(e => e.Event == filter.Event);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            entries = entries.Where(e => e.Category == filter.Category);
        }

        if (filter.Severity is { } severity)
        {
            entries = entries.Where(e => e.Severity == severity);
        }

        if (filter.Outcome is { } outcome)
        {
            entries = entries.Where(e => e.Outcome == outcome);
        }

        if (filter.Kind is { } kind)
        {
            entries = entries.Where(e => e.Kind == kind);
        }

        if (filter.UserId is { } userId)
        {
            entries = entries.Where(e => e.UserId == userId);
        }

        if (filter.PatientId is { } patientId)
        {
            entries = entries.Where(e => e.PatientId == patientId);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            entries = entries.Where(e => e.CorrelationId == filter.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            // Matching users first (a short list), so the log is searched by user_id = ANY(...) through its index,
            // OR'ed with the small partial index of failed sign-ins. Matching users inside the log query made
            // PostgreSQL scan every entry (4.4 quality pass: 288 ms → 5 ms on the Standard demo).
            var pattern = $"%{filter.Q.Trim()}%";
            var userIds = await db.Set<User>()
                .Where(u => EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.DisplayName, pattern))
                .Select(u => (Guid?)u.Id)
                .ToListAsync(cancellationToken);
            entries = entries.Where(e =>
                (e.AttemptedIdentifier != null && EF.Functions.ILike(e.AttemptedIdentifier, pattern))
                || userIds.Contains(e.UserId));
        }

        return entries;
    }

    private static async Task<List<AuditEntryResponse>> ToResponsesAsync(
        AppDbContext db,
        IQueryable<AuditEntry> entries,
        CancellationToken cancellationToken)
    {
        // A left join on the user's primary key. (FirstOrDefault in a projection made EF number every user with
        // ROW_NUMBER() on each page.)
        var rows = await (
                from e in entries
                join u in db.Set<User>().AsNoTracking() on e.UserId equals u.Id into matched
                from u in matched.DefaultIfEmpty()
                select new { Entry = e, User = u == null ? null : new { u.DisplayName, u.Email } })
            .ToListAsync(cancellationToken);

        var labels = AuditEvents.All.ToDictionary(e => e.Key, e => e.Label);
        return rows.Select(row => new AuditEntryResponse(
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
            row.Entry.ActingForPatientLinkId,
            row.Entry.EntityType,
            row.Entry.EntityId,
            ParseJson(row.Entry.Changes),
            ParseJson(row.Entry.Details),
            row.Entry.CorrelationId,
            row.Entry.IpAddress?.ToString(),
            row.Entry.UserAgent)).ToList();
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

/// <summary>RFC 4180 CSV, UTF-8 with a byte order mark so spreadsheet apps read names correctly.</summary>
internal static class AuditCsv
{
    private static readonly string[] Header =
    [
        "occurred_at", "event", "label", "kind", "category", "severity", "outcome", "user_id", "user_name", "user_email",
        "attempted_identifier", "patient_id", "acting_for_patient_link_id", "entity_type", "entity_id", "changes", "details",
        "correlation_id", "ip_address", "user_agent",
    ];

    public static byte[] Write(IEnumerable<AuditEntryResponse> rows)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);
        foreach (var e in rows)
        {
            AppendRow(csv,
            [
                e.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
                e.Event,
                e.Label,
                Snake(e.Kind),
                e.Category,
                Snake(e.Severity),
                Snake(e.Outcome),
                e.UserId?.ToString(),
                e.UserName,
                e.UserEmail,
                e.AttemptedIdentifier,
                e.PatientId?.ToString(),
                e.ActingForPatientLinkId?.ToString(),
                e.EntityType,
                e.EntityId?.ToString(),
                e.Changes?.GetRawText(),
                e.Details?.GetRawText(),
                e.CorrelationId,
                e.IpAddress,
                e.UserAgent,
            ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>Same values as the JSON responses, e.g. <c>data_change</c>.</summary>
    private static string Snake<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static void AppendRow(StringBuilder csv, string?[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                csv.Append(',');
            }

            csv.Append(Escape(fields[i]));
        }

        csv.Append("\r\n");
    }

    /// <summary>
    /// Quotes when needed, and defuses values a spreadsheet would run as a formula (a typed sign-in email such as
    /// <c>=HYPERLINK(…)</c> is attacker-controlled) by prefixing an apostrophe.
    /// </summary>
    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}

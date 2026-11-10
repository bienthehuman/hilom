using System.Text.Json;
using System.Text.Json.Serialization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Infrastructure.Auditing;

/// <summary>What a named event is about.</summary>
/// <param name="Type">Entity name, e.g. <c>User</c> or <c>Role</c>.</param>
public sealed record AuditTarget(string Type, Guid Id);

/// <summary>Options for one named event. All optional.</summary>
public sealed record AuditOptions
{
    public AuditTarget? Target { get; init; }

    /// <summary>Event-specific facts, serialized to JSON (e.g. before/after role lists). Never secrets.</summary>
    public object? Details { get; init; }

    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;

    public Guid? PatientId { get; init; }

    /// <summary>Who acted, when it isn't the signed-in user (e.g. the account that just signed in).</summary>
    public Guid? ActorId { get; init; }

    /// <summary>Email typed on a failed sign-in.</summary>
    public string? AttemptedIdentifier { get; init; }
}

/// <summary>
/// Records named audit events. <see cref="Record"/> adds the entry to the current unit of work, so it commits
/// together with the handler's own changes (docs/AUDIT.md, rule 2). Use <see cref="RecordNowAsync"/> only when
/// there is no other change to save (e.g. a failed sign-in or a denied request).
/// </summary>
public interface IAuditLog
{
    void Record(AuditEvent auditEvent, AuditOptions? options = null);

    Task RecordNowAsync(AuditEvent auditEvent, AuditOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Logs a sensitive read, such as opening a patient's record, and saves it immediately. Logged once per
    /// user, event and patient per 15 minutes (docs/AUDIT.md, rule 4): opening a
    /// chart five times in a minute is one entry. <see cref="AuditOptions.PatientId"/> is required.
    /// </summary>
    Task RecordReadAsync(AuditEvent auditEvent, AuditOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Say so when a request legitimately changed nothing (e.g. saving a role without edits), so the endpoint's
    /// <c>.Audited(...)</c> check doesn't mistake it for a forgotten audit entry.
    /// </summary>
    void NothingToRecord();

    /// <summary>Events recorded during this request; checked against the endpoint's <c>.Audited(...)</c> declaration.</summary>
    IReadOnlyCollection<AuditEvent> RecordedEvents { get; }

    bool DeclaredNothingToRecord { get; }
}

internal sealed class AuditLog(AppDbContext db, IHttpContextAccessor httpContextAccessor, TimeProvider time) : IAuditLog
{
    /// <summary>Repeat reads of the same patient by the same user within this window aren't logged again.</summary>
    public static readonly TimeSpan ReadDedupeWindow = TimeSpan.FromMinutes(15);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly List<AuditEvent> _recorded = [];

    public IReadOnlyCollection<AuditEvent> RecordedEvents => _recorded;

    public bool DeclaredNothingToRecord { get; private set; }

    public void NothingToRecord() => DeclaredNothingToRecord = true;

    public void Record(AuditEvent auditEvent, AuditOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        db.Add(CreateEntry(auditEvent, AuditKind.Event, options ?? new AuditOptions()));
        _recorded.Add(auditEvent);
    }

    public async Task RecordNowAsync(AuditEvent auditEvent, AuditOptions? options, CancellationToken cancellationToken)
    {
        Record(auditEvent, options);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordReadAsync(AuditEvent auditEvent, AuditOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentNullException.ThrowIfNull(options);
        if (options.PatientId is not { } patientId)
        {
            throw new ArgumentException("A sensitive read must name the patient.", nameof(options));
        }

        var entry = CreateEntry(auditEvent, AuditKind.Read, options);
        var since = entry.OccurredAt - ReadDedupeWindow;
        var seenRecently = await db.Set<AuditEntry>().AnyAsync(
            e => e.UserId == entry.UserId
                && e.Event == entry.Event
                && e.PatientId == patientId
                && e.OccurredAt >= since,
            cancellationToken);
        if (seenRecently)
        {
            return;
        }

        db.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuditEntry CreateEntry(AuditEvent auditEvent, AuditKind kind, AuditOptions options)
    {
        var entry = new AuditEntry(
            auditEvent,
            kind,
            options.Outcome,
            time.GetUtcNow(),
            AuditRequestInfo.From(httpContextAccessor.HttpContext))
        {
            AttemptedIdentifier = options.AttemptedIdentifier,
            PatientId = options.PatientId,
            EntityType = options.Target?.Type,
            EntityId = options.Target?.Id,
            Details = options.Details is null ? null : JsonSerializer.Serialize(options.Details, Json),
        };
        if (options.ActorId is not null)
        {
            entry.UserId = options.ActorId;
        }

        return entry;
    }
}

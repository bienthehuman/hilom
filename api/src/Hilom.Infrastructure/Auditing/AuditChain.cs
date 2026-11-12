using System.Security.Cryptography;
using System.Text.Json;
using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Infrastructure.Auditing;

/// <summary>What <see cref="AuditChain.VerifyAsync"/> found.</summary>
/// <param name="Intact">True when every sealed entry still matches its hash and links to the one before it.</param>
/// <param name="VerifiedCount">Sealed entries that checked out, in chain order: all of them when intact, otherwise those before the first break.</param>
/// <param name="UnsealedCount">Entries written since the last sealing run, not yet in the chain.</param>
/// <param name="LastSealedAt">When the newest sealed entry was sealed.</param>
/// <param name="FirstBreak">The first entry where the chain doesn't hold, if any.</param>
public sealed record AuditChainReport(
    bool Intact,
    long VerifiedCount,
    long UnsealedCount,
    DateTimeOffset? LastSealedAt,
    AuditChainBreak? FirstBreak);

/// <param name="Position">1-based position in the chain.</param>
/// <param name="Reason">
/// <c>entry_altered</c>: the entry's contents no longer match its hash.
/// <c>link_broken</c>: its <c>prev_hash</c> isn't the previous entry's hash, so an entry before it was removed,
/// inserted or re-hashed. <c>seal_incomplete</c>: marked sealed without a hash.
/// </param>
public sealed record AuditChainBreak(Guid EntryId, DateTimeOffset OccurredAt, string Event, long Position, string Reason);

/// <summary>
/// Tamper evidence for <c>access.audit_log</c> (docs/AUDIT.md, rule 6). Sealing links entries into a hash chain,
/// <c>hash = sha256(prev_hash ‖ entry)</c>, in the order they were sealed: by <c>sealed_at</c>, then
/// <c>occurred_at</c>, then <c>id</c>. Each run uses one <c>sealed_at</c>, later than every earlier run's, so that order
/// is fixed even for an entry whose transaction committed after later entries were sealed.
/// The first entry links to 32 zero bytes.
/// </summary>
public sealed class AuditChain(AppDbContext db, TimeProvider time)
{
    public const int BatchSize = 1000;

    /// <summary><c>prev_hash</c> of the first entry in the chain.</summary>
    public static readonly byte[] Genesis = new byte[SHA256.HashSizeInBytes];

    // pg_advisory_xact_lock key, so two app instances never seal at once ("audit" in ASCII).
    private const long SealLockKey = 0x6175646974;

    // The entry as hashed: every column except the sealing ones, as PostgreSQL prints it, so the hash doesn't depend
    // on how the app maps types. jsonb prints in a canonical form. (Aliases are snake_case: the naming convention
    // maps them to the row classes below.)
    private const string EntryColumns = """
        id,
        to_char(occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS occurred_at_text,
        kind,
        event,
        category,
        severity,
        outcome,
        user_id::text AS user_id,
        attempted_identifier::text AS attempted_identifier,
        patient_id::text AS patient_id,
        acting_for_patient_link_id::text AS acting_for_patient_link_id,
        entity_type,
        entity_id::text AS entity_id,
        changes::text AS changes,
        details::text AS details,
        correlation_id,
        ip_address::text AS ip_address,
        user_agent
        """;

    /// <summary>
    /// Seals every entry not yet in the chain, in batches. Returns how many were sealed, or 0 when another instance
    /// holds the lock.
    /// </summary>
    public async Task<int> SealAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var sealedNow = await SealBatchAsync(cancellationToken);
            total += Math.Max(sealedNow, 0);
            if (sealedNow < BatchSize)
            {
                return total;
            }
        }
    }

    /// <summary>Walks the whole chain and reports the first entry that doesn't hold.</summary>
    public async Task<AuditChainReport> VerifyAsync(CancellationToken cancellationToken)
    {
        var unsealed = await db.Set<AuditEntry>().LongCountAsync(e => e.SealedAt == null, cancellationToken);
        var lastSealedAt = await db.Set<AuditEntry>().MaxAsync(e => e.SealedAt, cancellationToken);

        var rows = db.Database.SqlQueryRaw<SealedRow>($"""
            SELECT {EntryColumns}, prev_hash, hash, occurred_at
            FROM access.audit_log AS a
            WHERE a.sealed_at IS NOT NULL
            ORDER BY a.sealed_at, a.occurred_at, a.id
            """).AsAsyncEnumerable();

        var previous = Genesis;
        long position = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            position++;
            var reason = row.Hash is null || row.PrevHash is null ? "seal_incomplete"
                : !row.PrevHash.AsSpan().SequenceEqual(previous) ? "link_broken"
                : !HashOf(row.PrevHash, row).AsSpan().SequenceEqual(row.Hash) ? "entry_altered"
                : null;

            if (reason is not null)
            {
                return new AuditChainReport(
                    false,
                    position - 1,
                    unsealed,
                    lastSealedAt,
                    new AuditChainBreak(row.Id, row.OccurredAt, row.Event, position, reason));
            }

            previous = row.Hash!;
        }

        return new AuditChainReport(true, position, unsealed, lastSealedAt, null);
    }

    /// <summary><c>sha256(prev_hash ‖ entry)</c>, the entry written as a JSON array of its column values.</summary>
    internal static byte[] HashOf(byte[] prevHash, EntryRow row)
    {
        using var buffer = new MemoryStream();
        buffer.Write(prevHash);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            json.WriteStringValue(row.Id);
            foreach (var value in (string?[])
                [
                    row.OccurredAtText, row.Kind, row.Event, row.Category, row.Severity, row.Outcome, row.UserId,
                    row.AttemptedIdentifier, row.PatientId, row.ActingForPatientLinkId, row.EntityType, row.EntityId,
                    row.Changes, row.Details, row.CorrelationId, row.IpAddress, row.UserAgent,
                ])
            {
                if (value is null)
                {
                    json.WriteNullValue();
                }
                else
                {
                    json.WriteStringValue(value);
                }
            }

            json.WriteEndArray();
        }

        return SHA256.HashData(buffer.ToArray());
    }

    /// <returns>Entries sealed, or -1 when another instance is sealing.</returns>
    private async Task<int> SealBatchAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var locked = await db.Database
            .SqlQuery<bool>($"""SELECT pg_try_advisory_xact_lock({SealLockKey}) AS "Value" """)
            .SingleAsync(cancellationToken);
        if (!locked)
        {
            return -1;
        }

        var last = await db.Database.SqlQueryRaw<ChainTip>("""
            SELECT hash, sealed_at
            FROM access.audit_log
            WHERE sealed_at IS NOT NULL
            ORDER BY sealed_at DESC, occurred_at DESC, id DESC
            LIMIT 1
            """).SingleOrDefaultAsync(cancellationToken);

        var batch = await db.Database.SqlQueryRaw<EntryRow>($"""
            SELECT {EntryColumns}
            FROM access.audit_log AS a
            WHERE a.sealed_at IS NULL
            ORDER BY a.occurred_at, a.id
            LIMIT {BatchSize}
            """).ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            return 0;
        }

        // One timestamp per run, strictly after the previous run's, so the chain order can't tie across runs.
        var now = TruncateToMicroseconds(time.GetUtcNow());
        var sealedAt = last?.SealedAt is { } previousRun && previousRun >= now ? previousRun.AddTicks(10) : now;

        var previous = last?.Hash ?? Genesis;
        var ids = new Guid[batch.Count];
        var prevHashes = new byte[batch.Count][];
        var hashes = new byte[batch.Count][];
        for (var i = 0; i < batch.Count; i++)
        {
            ids[i] = batch[i].Id;
            prevHashes[i] = previous;
            hashes[i] = previous = HashOf(previous, batch[i]);
        }

        // The append-only trigger allows exactly this: filling the sealing columns once, nothing else.
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE access.audit_log AS a
            SET prev_hash = s.prev_hash, hash = s.hash, sealed_at = {sealedAt}
            FROM unnest({ids}, {prevHashes}, {hashes}) AS s(id, prev_hash, hash)
            WHERE a.id = s.id AND a.sealed_at IS NULL
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);

    internal class EntryRow
    {
        public Guid Id { get; init; }

        public string OccurredAtText { get; init; } = "";

        public string Kind { get; init; } = "";

        public string Event { get; init; } = "";

        public string Category { get; init; } = "";

        public string Severity { get; init; } = "";

        public string Outcome { get; init; } = "";

        public string? UserId { get; init; }

        public string? AttemptedIdentifier { get; init; }

        public string? PatientId { get; init; }

        public string? ActingForPatientLinkId { get; init; }

        public string? EntityType { get; init; }

        public string? EntityId { get; init; }

        public string? Changes { get; init; }

        public string? Details { get; init; }

        public string CorrelationId { get; init; } = "";

        public string? IpAddress { get; init; }

        public string? UserAgent { get; init; }
    }

    private sealed class SealedRow : EntryRow
    {
        public byte[]? PrevHash { get; init; }

        public byte[]? Hash { get; init; }

        public DateTimeOffset OccurredAt { get; init; }
    }

    private sealed class ChainTip
    {
        public byte[]? Hash { get; init; }

        public DateTimeOffset? SealedAt { get; init; }
    }
}

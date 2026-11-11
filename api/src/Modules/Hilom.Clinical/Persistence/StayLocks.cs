using System.Buffers.Binary;
using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Persistence;

/// <summary>
/// Serializes admitting, transferring and discharging that touch the same patient or bed. Two racing inserts
/// checked by one exclusion constraint can deadlock in PostgreSQL (each waits on the other's uncommitted row),
/// and the loser would get a 500 instead of a 409. Holding these locks for the rest of the transaction makes
/// "check, then write" one step: the second request waits, then its checks see the first one's committed rows.
/// The exclusion constraints stay as the final guard.
/// </summary>
internal static class StayLocks
{
    /// <summary>Our own key space for the two-key advisory lock form, so these never collide with other locks.</summary>
    private const int ClassId = 0x48535459; // "HSTY"

    /// <summary>Locks each patient or bed id until the current transaction ends. Call inside a transaction.</summary>
    public static async Task LockAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        // Always in the same order, so two requests locking the same pair can't deadlock on the locks themselves.
        foreach (var key in ids.Select(KeyOf).Distinct().Order())
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ClassId}, {key})", cancellationToken);
        }
    }

    /// <summary>A 32-bit key from the id. A collision only makes two unrelated requests wait for each other.</summary>
    private static int KeyOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes)
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }
}

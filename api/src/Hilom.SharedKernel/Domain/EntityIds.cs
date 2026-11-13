namespace Hilom.SharedKernel.Domain;

/// <summary>
/// Where new entities get their ids. Normally a time-ordered UUID v7. Demo data generation swaps in ids drawn
/// from its seeded random generator for the duration of a run, so the same seed produces the same ids.
/// The override follows the async flow of the caller that set it, so requests in the same process aren't affected.
/// </summary>
public static class EntityIds
{
    private static readonly AsyncLocal<Func<Guid>?> Override = new();

    public static Guid Next() => (Override.Value ?? Guid.CreateVersion7)();

    /// <summary>Uses <paramref name="next"/> for every id created until the returned scope is disposed.</summary>
    public static IDisposable Use(Func<Guid> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var previous = Override.Value;
        Override.Value = next;
        return new Restore(previous);
    }

    private sealed class Restore(Func<Guid>? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}

namespace Hilom.DemoData;

/// <summary>
/// The one source of randomness for a run. It comes from the seed alone, so a run is repeatable; entity ids are drawn
/// from it too (UUID v7 layout: the moment they were "created", then random bits).
/// </summary>
public sealed class DemoRandom(int seed)
{
    private readonly Random _random = new(seed);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();

    public bool Chance(double probability) => _random.NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    /// <summary>Picks by weight: <c>(item, weight)</c> pairs, heavier more likely.</summary>
    public T Pick<T>(params (T Item, double Weight)[] choices)
    {
        var roll = _random.NextDouble() * choices.Sum(c => c.Weight);
        foreach (var (item, weight) in choices)
        {
            roll -= weight;
            if (roll < 0)
            {
                return item;
            }
        }

        return choices[^1].Item;
    }

    /// <summary>A time within <paramref name="from"/> (inclusive) and <paramref name="to"/>, to the minute.</summary>
    public DateTimeOffset Between(DateTimeOffset from, DateTimeOffset to)
    {
        var minutes = (int)Math.Max(1, (to - from).TotalMinutes);
        return from.AddMinutes(_random.Next(minutes));
    }

    /// <summary>A UUID v7-shaped id for the given moment.</summary>
    public Guid NewId(DateTimeOffset at)
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        var ms = (ulong)Math.Max(0, at.ToUnixTimeMilliseconds());
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}

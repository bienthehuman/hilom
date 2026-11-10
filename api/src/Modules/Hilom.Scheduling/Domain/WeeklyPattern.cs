namespace Hilom.Scheduling.Domain;

/// <summary>
/// "Mondays and Thursdays, 09:00–12:00, 30-minute slots, for the next four weeks", in hospital time.
/// A time window that doesn't divide evenly leaves the remainder unused.
/// </summary>
public sealed record WeeklyPattern(
    DateOnly From,
    DateOnly To,
    IReadOnlySet<DayOfWeek> Days,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotMinutes)
{
    public const int MinSlotMinutes = 5;
    public const int MaxSlotMinutes = 240;

    /// <summary>A pattern covers at most this many days.</summary>
    public const int MaxDays = 84;

    /// <summary>One publish creates at most this many slots.</summary>
    public const int MaxSlots = 500;

    /// <summary>Every slot the pattern describes, as UTC instants, in time order.</summary>
    public IEnumerable<(DateTimeOffset StartsAt, DateTimeOffset EndsAt)> Slots(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var length = TimeSpan.FromMinutes(SlotMinutes);
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            if (!Days.Contains(day.DayOfWeek))
            {
                continue;
            }

            var dayEnd = day.ToDateTime(EndTime);
            for (var start = day.ToDateTime(StartTime); start + length <= dayEnd; start += length)
            {
                yield return (ToUtc(start, timeZone), ToUtc(start + length, timeZone));
            }
        }
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), timeZone), TimeSpan.Zero);
}

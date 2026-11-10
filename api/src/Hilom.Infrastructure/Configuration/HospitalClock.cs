using Microsoft.Extensions.Options;

namespace Hilom.Infrastructure.Configuration;

/// <summary>Dates as the hospital sees them. "Today" turns over at midnight in the hospital's time zone, not UTC.</summary>
public sealed class HospitalClock(TimeProvider time, IOptions<HospitalOptions> options)
{
    public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZone).DateTime);
}

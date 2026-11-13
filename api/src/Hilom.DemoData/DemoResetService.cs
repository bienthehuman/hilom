using Hilom.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hilom.DemoData;

/// <summary>Wipes and regenerates the demo every night, so nothing a visitor did outlasts the day.</summary>
internal sealed partial class DemoResetService(
    IServiceProvider services,
    IOptions<DemoOptions> options,
    HospitalClock hospital,
    TimeProvider time,
    ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.NightlyReset || !DemoOptions.TryParseResetAt(options.Value.ResetAt, out var resetAt))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = NextResetAfter(time.GetUtcNow(), resetAt, hospital.TimeZone);
            LogScheduled(next);
            try
            {
                await Task.Delay(next - time.GetUtcNow(), time, stoppingToken);
                await DemoDatabase.ResetAsync(services, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed reset must not stop the job: the next night tries again.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogFailed(exception);
                await Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken);
            }
        }
    }

    /// <summary>The next time the clock in the hospital reads <paramref name="resetAt"/>, strictly after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextResetAfter(DateTimeOffset now, TimeOnly resetAt, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        for (var offset = 0; offset <= 2; offset++)
        {
            var day = DateOnly.FromDateTime(local.DateTime).AddDays(offset);
            var candidate = day.ToDateTime(resetAt);
            // A time that doesn't exist on the day clocks change (spring forward) is skipped to the next day.
            if (zone.IsInvalidTime(candidate))
            {
                continue;
            }

            var instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
            if (instant > now)
            {
                return instant;
            }
        }

        throw new InvalidOperationException("No reset time found in the next two days.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The next demo reset is at {At:u}")]
    private partial void LogScheduled(DateTimeOffset at);

    [LoggerMessage(Level = LogLevel.Error, Message = "The nightly demo reset failed; trying again in five minutes")]
    private partial void LogFailed(Exception exception);
}

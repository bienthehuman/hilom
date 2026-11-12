using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hilom.Infrastructure.Auditing;

/// <summary>Settings for the audit sealing job (section <c>Audit:Sealing</c>).</summary>
public sealed class AuditSealingOptions
{
    public const string SectionName = "Audit:Sealing";

    /// <summary>Off in integration tests, which seal on demand so the chain is predictable.</summary>
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Links new audit entries into the hash chain every minute (docs/AUDIT.md, rule 6).</summary>
internal sealed partial class AuditSealingService(
    IServiceScopeFactory scopes,
    IOptions<AuditSealingOptions> options,
    TimeProvider time,
    ILogger<AuditSealingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sealedCount = await scope.ServiceProvider.GetRequiredService<AuditChain>().SealAsync(stoppingToken);
                if (sealedCount > 0)
                {
                    LogSealed(sealedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // One failed run (e.g. the database restarting) must not stop the job; the next run catches up.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sealed {Count} audit entries into the hash chain")]
    private partial void LogSealed(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sealing the audit log failed; the next run will retry")]
    private partial void LogFailed(Exception exception);
}

using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hilom.DemoData;

/// <summary>
/// Generates the fictional hospital into an empty database, once. Registered only in demo mode. It runs after the
/// platform seeders (roles, first administrator) and leaves a database that already holds records alone.
/// </summary>
internal sealed partial class DemoSeeder(
    AppDbContext db,
    DemoGenerator generator,
    IOptions<DemoOptions> options,
    ILogger<DemoSeeder> logger) : IDataSeeder
{
    public int Order => 900;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await DemoDatabase.IsDemoAsync(db, cancellationToken))
        {
            return;
        }

        if (await DemoDatabase.HasRecordsAsync(db, cancellationToken))
        {
            LogNotEmpty(logger);
            return;
        }

        await generator.RunAsync(cancellationToken);
        await DemoDatabase.MarkAsync(db, options.Value, DateTimeOffset.UtcNow, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo mode is on, but this database already holds patients or staff. Not adding demo data to it.")]
    private static partial void LogNotEmpty(ILogger logger);
}

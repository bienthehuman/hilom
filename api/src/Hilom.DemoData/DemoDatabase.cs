using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hilom.DemoData;

/// <summary>
/// A marker table says "this database holds generated demo data". Nothing wipes a database without it, and nothing
/// generates into a database that already holds someone's real records.
/// </summary>
internal static partial class DemoDatabase
{
    public static async Task<bool> IsDemoAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<bool>($"SELECT to_regclass('public.demo_dataset') IS NOT NULL AS \"Value\"")
            .SingleAsync(cancellationToken)
        && await db.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM public.demo_dataset) AS \"Value\"")
            .SingleAsync(cancellationToken);

    public static async Task MarkAsync(AppDbContext db, DemoOptions options, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync($"""
            CREATE TABLE IF NOT EXISTS public.demo_dataset (
                id int PRIMARY KEY CHECK (id = 1),
                seed int NOT NULL,
                scale text NOT NULL,
                generated_at timestamptz NOT NULL)
            """, cancellationToken);
        var scale = options.Scale.ToString();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.demo_dataset (id, seed, scale, generated_at) VALUES (1, {options.Seed}, {scale}, {generatedAt})
            ON CONFLICT (id) DO UPDATE SET seed = excluded.seed, scale = excluded.scale, generated_at = excluded.generated_at
            """, cancellationToken);
    }

    /// <summary>Does the database already hold records that weren't generated here?</summary>
    public static async Task<bool> HasRecordsAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<bool>($"SELECT (EXISTS (SELECT 1 FROM patients.patients) OR EXISTS (SELECT 1 FROM workforce.employees)) AS \"Value\"")
            .SingleAsync(cancellationToken);

    /// <summary>
    /// Drops every module schema and the migration history, then migrates and seeds again from nothing. Dropping is
    /// the one thing the append-only audit log allows: its triggers refuse row changes, not removal of the schema.
    /// </summary>
    public static async Task ResetAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await IsDemoAsync(db, cancellationToken))
        {
            throw new InvalidOperationException("Refusing to reset: this database isn't marked as generated demo data.");
        }

        var state = services.GetRequiredService<DemoState>();
        state.Resetting = true;
        try
        {
            LogResetting(logger);
            var schemas = scope.ServiceProvider.GetRequiredService<ModuleRegistry>().Modules.Select(m => m.Schema).Distinct().ToList();
            foreach (var schema in schemas)
            {
                // Schema names are the modules' own constants (lowercase letters and underscores, checked by a test).
#pragma warning disable EF1002, CA2100
                await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE", cancellationToken);
#pragma warning restore EF1002, CA2100
            }

            await db.Database.ExecuteSqlAsync($"DROP TABLE IF EXISTS public.demo_dataset", cancellationToken);
            await db.Database.ExecuteSqlAsync($"DROP TABLE IF EXISTS public.__ef_migrations_history", cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            await Hilom.Infrastructure.Seeding.DataSeeding.RunSeedersAsync(services, cancellationToken);
            LogResetDone(logger);
        }
        finally
        {
            state.Resetting = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Resetting the demo: dropping everything and generating it again")]
    private static partial void LogResetting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The demo has been reset")]
    private static partial void LogResetDone(ILogger logger);
}

namespace Hilom.Infrastructure.Seeding;

/// <summary>
/// Inserts required starting data (e.g. the first System Administrator). Must be safe to run repeatedly.
/// Runs on startup in development and in tests; deployments run it as an explicit step.
/// </summary>
public interface IDataSeeder
{
    /// <summary>Lower runs first. Use the module order: Access 100, Facility 200, Workforce 300…</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

using Hilom.Infrastructure;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace Hilom.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API against a fresh PostgreSQL 18 container, migrated once per test run.
/// Tests that write data call <see cref="ResetDatabaseAsync"/> first.
/// </summary>
public sealed class HilomApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();
    private Respawner? _respawner;

    public string ConnectionString => _database.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    /// <summary>Deletes all rows from module tables, keeping the schema and migration history.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (!await HasModuleTablesAsync(connection))
        {
            return;
        }

        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [.. ModuleSchemas()],
            TablesToIgnore = [new Table("public", DependencyInjection.MigrationsHistoryTable)],
        });

        await _respawner.ResetAsync(connection);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DatabaseConnection.ConnectionStringName}", ConnectionString);
    }

    private string[] ModuleSchemas() =>
        [.. Services.GetRequiredService<ModuleRegistry>().Modules.Select(m => m.Schema)];

    // Respawn refuses to run against a database with no tables, which is the case until modules add entities.
    private async Task<bool> HasModuleTablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "select exists (select 1 from information_schema.tables where table_schema = any(@schemas))", connection);
        command.Parameters.AddWithValue("schemas", ModuleSchemas());
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<HilomApiFactory>
{
    public const string Name = "api";
}

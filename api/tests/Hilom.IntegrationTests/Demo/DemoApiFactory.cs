using Hilom.DemoData;
using Hilom.Infrastructure.Persistence;
using Hilom.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Hilom.IntegrationTests.Demo;

/// <summary>
/// The real API in demo mode against its own PostgreSQL container: it migrates and generates a tiny hospital at startup.
/// Separate from the shared factory because demo tests reset the whole database.
/// </summary>
public sealed class DemoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const int Seed = 7;
    public const string Password = "Demo-Test-Password-1";

    /// <summary>Fixed, and in the past, so nothing the tests read depends on when they run.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();

    public string ConnectionString => _database.GetConnectionString();

    public DemoOptions Options => Services.GetRequiredService<IOptions<DemoOptions>>().Value;

    /// <summary>Starts the database, then the app, which migrates and generates before the first test looks at either.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        _ = Services;
    }

    public HttpClient CreateBrowserClient() =>
        CreateDefaultClient(new AntiforgeryHandler(), new CookieContainerHandler());

    /// <summary>Wipes and regenerates, as the nightly job does.</summary>
    public async Task ResetAsync()
    {
        using var logger = LoggerFactory.Create(_ => { });
        await DemoDatabase.ResetAsync(Services, logger.CreateLogger("demo-reset"), TestContext.Current.CancellationToken);
    }

    /// <summary>One value per table that changes if any generated id, name, number or time changes.</summary>
    public async Task<string> FingerprintAsync()
    {
        // The only rows stamped by the real clock are the ones made by startup seeding (the first administrator).
        var simulated = $"occurred_at <= '{Now:O}'";
        var sql = $"""
            SELECT concat_ws(' ',
              (SELECT md5(string_agg(concat_ws('|', id, mrn, first_name, last_name, birth_date, address), ',' ORDER BY id)) FROM patients.patients),
              (SELECT md5(string_agg(concat_ws('|', id, employee_no, first_name, last_name, user_id), ',' ORDER BY id)) FROM workforce.employees),
              (SELECT md5(string_agg(concat_ws('|', id, slot_id, patient_id, status, booked_by), ',' ORDER BY id)) FROM scheduling.appointments),
              (SELECT md5(string_agg(concat_ws('|', id, started_at, ended_at, status), ',' ORDER BY id)) FROM clinical.encounters),
              (SELECT md5(string_agg(concat_ws('|', id, temperature_c, heart_rate, recorded_at), ',' ORDER BY id)) FROM clinical.vital_signs),
              (SELECT md5(string_agg(concat_ws('|', id, order_id, outcome, administered_at), ',' ORDER BY id)) FROM clinical.medication_administrations),
              (SELECT md5(string_agg(concat_ws('|', id, event, occurred_at, user_id, patient_id), ',' ORDER BY id)) FROM access.audit_log WHERE {simulated}))
            """;
        return await ScalarAsync<string>(sql);
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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
        builder.UseSetting("Access:SeedAdmin:Email", "admin@hilom.test");
        builder.UseSetting("Access:SeedAdmin:Password", "Integration-Admin-2026");
        builder.UseSetting("Access:LoginAttemptsPerMinute", "1000");
        builder.UseSetting("Email:AppUrl", "https://hilom.test");
        builder.UseSetting("Audit:Sealing:Enabled", "false");
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("Demo:Scale", nameof(DemoScale.Tiny));
        builder.UseSetting("Demo:Seed", Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Demo:Now", Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Demo:Password", Password);
        builder.UseSetting("Demo:NightlyReset", "false");
    }
}

using Hilom.Access.Domain;
using Hilom.Infrastructure;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace Hilom.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API against a fresh PostgreSQL 18 container, migrated and seeded once per test run.
/// Tests create their own uniquely named data, or call <see cref="ResetDatabaseAsync"/> first.
/// </summary>
public sealed class HilomApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@hilom.test";
    public const string AdminPassword = "Integration-Admin-2026";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18").Build();
    private Respawner? _respawner;

    public string ConnectionString => _database.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await Services.RunSeedersAsync();
    }

    /// <summary>
    /// A client that behaves like the browser: keeps cookies and sends the antiforgery token.
    /// Call <see cref="ApiClientExtensions.PrimeAntiforgeryAsync"/> before the first POST.
    /// </summary>
    public HttpClient CreateBrowserClient() =>
        CreateDefaultClient(new AntiforgeryHandler(), new CookieContainerHandler());

    /// <summary>Adds a user straight to the database, optionally holding roles (by name).</summary>
    public async Task<User> CreateUserAsync(string password, bool disabled = false, params string[] roles)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var user = new User($"user-{Guid.NewGuid():N}@hilom.test", "Test User");
        user.ChangePassword(hasher.HashPassword(user, password));
        if (disabled)
        {
            user.Disable();
        }

        db.Add(user);
        var roleIds = await db.Set<Role>().Where(r => roles.Contains(r.Name)).Select(r => r.Id).ToListAsync();
        roleIds.Count.ShouldBe(roles.Length, "every requested role must exist");
        foreach (var roleId in roleIds)
        {
            db.Add(new UserRole(user.Id, roleId, null, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Signs in a new user who holds the given roles, and returns their client.</summary>
    public async Task<(HttpClient Client, User User)> SignInWithRolesAsync(params string[] roles)
    {
        const string password = "Role-Test-Password-1";
        var user = await CreateUserAsync(password, disabled: false, roles);
        var client = CreateBrowserClient();
        await client.SignInAsync(user.Email, password);
        return (client, user);
    }

    /// <summary>Creates a role granting the given permissions at <c>all</c>, and returns its name.</summary>
    public async Task<string> CreateRoleAsync(params string[] permissions)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = Role.Create(
            $"Test role {Guid.NewGuid():N}",
            "",
            permissions.Select(p => new RoleGrant(p, Hilom.SharedKernel.Authorization.PermissionScope.All))).Value;
        db.Add(role);
        await db.SaveChangesAsync();
        return role.Name;
    }

    public async Task<Guid> RoleIdAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<Role>().Where(r => r.Name == name).Select(r => r.Id).SingleAsync();
    }

    /// <summary>Runs a change against the database outside any request, e.g. disabling a user mid-session.</summary>
    public async Task UpdateUserAsync(Guid userId, Action<User> change)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Set<User>().SingleAsync(u => u.Id == userId);
        change(user);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes all rows from module tables (keeping schema and migration history), then re-seeds
    /// starting data such as the System Administrator.
    /// </summary>
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
        await Services.RunSeedersAsync();
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
        builder.UseSetting("Access:SeedAdmin:Email", AdminEmail);
        builder.UseSetting("Access:SeedAdmin:Password", AdminPassword);
        builder.UseSetting("Access:LoginAttemptsPerMinute", "1000");
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

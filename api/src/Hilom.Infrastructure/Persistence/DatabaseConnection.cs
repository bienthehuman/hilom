using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Hilom.Infrastructure.Persistence;

/// <summary>
/// Works out the database connection strings. There are two logins (docs/AUDIT.md, rule 5):
/// <list type="bullet">
/// <item>the <b>app</b> login (<c>ConnectionStrings:Hilom</c>), which serves requests and runs background jobs. It acts
/// through the <see cref="AppRole"/> role: reads and writes module tables, but only <c>SELECT</c> and <c>INSERT</c> on
/// <c>access.audit_log</c> (plus filling the sealing columns once).</item>
/// <item>the <b>owner</b> login (<c>ConnectionStrings:HilomOwner</c>), which owns the schema: it migrates, seeds and
/// resets the demo. When it isn't set, the app login does that work too.</item>
/// </list>
/// </summary>
public static class DatabaseConnection
{
    public const string ConnectionStringName = "Hilom";

    public const string OwnerConnectionStringName = "HilomOwner";

    /// <summary>The restricted role the app runs as. Created (without a login) by the <c>AddAppRole</c> migration.</summary>
    public const string AppRole = "hilom_app";

    /// <summary>The app's runtime connection.</summary>
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment) =>
        Resolve(configuration, environment, DatabaseLogin.App);

    /// <summary>
    /// Uses <c>ConnectionStrings:Hilom</c> (app) or <c>ConnectionStrings:HilomOwner</c> (owner) when set; an unset owner
    /// falls back to the app's. In development it falls back to the same <c>HILOM_DB_*</c> values Docker Compose reads
    /// from <c>.env</c>, so both always agree: <c>HILOM_DB_USER</c> is the owner, and the app logs in as
    /// <see cref="AppRole"/> with <c>HILOM_DB_APP_PASSWORD</c> (deploy/db/create-app-login.sh gives it that login).
    /// </summary>
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment, DatabaseLogin login)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var app = configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(app))
        {
            var owner = configuration.GetConnectionString(OwnerConnectionStringName);
            return login == DatabaseLogin.Owner && !string.IsNullOrWhiteSpace(owner) ? owner : app;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} must be set outside development.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = configuration["HILOM_DB_HOST"] ?? "localhost",
            Port = int.TryParse(configuration["HILOM_DB_PORT"], out var port) ? port : 5432,
            Database = configuration["HILOM_DB_NAME"] ?? "hilom",
            Username = configuration["HILOM_DB_USER"] ?? "hilom",
            Password = configuration["HILOM_DB_PASSWORD"] ?? "hilom_dev",
        };

        // Without HILOM_DB_APP_PASSWORD (an older .env) the app keeps connecting as the owner.
        if (login == DatabaseLogin.App && configuration["HILOM_DB_APP_PASSWORD"] is { Length: > 0 } appPassword)
        {
            builder.Username = AppRole;
            builder.Password = appPassword;
        }

        return builder.ConnectionString;
    }
}

/// <summary>Which login an <see cref="AppDbContext"/> connects with.</summary>
public enum DatabaseLogin
{
    App,
    Owner,
}

/// <summary>
/// Set once per scope, before the scope's <see cref="AppDbContext"/> is created. Everything runs as
/// <see cref="DatabaseLogin.App"/> unless it asks for the owner through <see cref="OwnerScope.CreateOwnerScope"/>.
/// </summary>
public sealed class DatabaseLoginChoice
{
    public DatabaseLogin Login { get; set; } = DatabaseLogin.App;
}

public static class OwnerScope
{
    /// <summary>
    /// A scope whose database context connects as the schema owner: for migrating, seeding and resetting the demo,
    /// never for serving requests.
    /// </summary>
    public static AsyncServiceScope CreateOwnerScope(this IServiceProvider services)
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<DatabaseLoginChoice>().Login = DatabaseLogin.Owner;
        return scope;
    }
}

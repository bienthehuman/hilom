using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Hilom.Infrastructure.Persistence;

/// <summary>Works out the database connection string.</summary>
public static class DatabaseConnection
{
    public const string ConnectionStringName = "Hilom";

    /// <summary>
    /// Uses <c>ConnectionStrings:Hilom</c> when set. In development it falls back to the same
    /// <c>HILOM_DB_*</c> values Docker Compose reads from <c>.env</c>, so both always agree.
    /// </summary>
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var explicitConnectionString = configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
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

        return builder.ConnectionString;
    }
}

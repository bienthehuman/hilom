using Hilom.Infrastructure.Persistence;
using Npgsql;

namespace Hilom.IntegrationTests.Infrastructure;

/// <summary>
/// Gives the restricted app role a login in a test container, as deploy/db/create-app-login.sh does for Docker Compose,
/// so the API under test runs as <see cref="DatabaseConnection.AppRole"/> while migrations and seeding run as the owner.
/// </summary>
public static class AppLogin
{
    public const string Password = "integration-app";

    public static async Task CreateAsync(string ownerConnectionString)
    {
        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"CREATE ROLE {DatabaseConnection.AppRole} LOGIN PASSWORD '{Password}'", connection);
        await command.ExecuteNonQueryAsync();
    }

    public static string ConnectionString(string ownerConnectionString) =>
        new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = DatabaseConnection.AppRole,
            Password = Password,
        }.ConnectionString;
}

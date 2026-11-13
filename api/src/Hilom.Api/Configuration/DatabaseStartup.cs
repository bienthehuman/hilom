using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Api.Configuration;

internal static partial class DatabaseStartup
{
    /// <summary>Set to <c>true</c> to start from an empty database. Only honoured for a database named <c>*_e2e</c>.</summary>
    public const string ResetSetting = "HILOM_DB_RESET_ON_START";

    private const string ResettableSuffix = "_e2e";

    /// <summary>
    /// Applies pending migrations. Used in development only; deployments migrate as a separate step.
    /// End-to-end runs set <see cref="ResetSetting"/> so every run starts from a fresh database (playwright.config.ts).
    /// The reset refuses any database whose name doesn't end in <c>_e2e</c>, so it can never wipe the development data.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateOwnerScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (app.Configuration.GetValue<bool>(ResetSetting))
        {
            var name = db.Database.GetDbConnection().Database;
            if (name.EndsWith(ResettableSuffix, StringComparison.Ordinal))
            {
                LogResetting(app.Logger, name);
                await db.Database.EnsureDeletedAsync();
            }
            else
            {
                LogResetRefused(app.Logger, ResetSetting, name, ResettableSuffix);
            }
        }

        await db.Database.MigrateAsync();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropping database {Database} to start the end-to-end run fresh")]
    private static partial void LogResetting(ILogger logger, string database);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Setting} ignored: database {Database} doesn't end in {Suffix}")]
    private static partial void LogResetRefused(ILogger logger, string setting, string database, string suffix);
}

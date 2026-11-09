using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Api.Configuration;

internal static class DatabaseStartup
{
    /// <summary>Applies pending migrations. Used in development only; deployments migrate as a separate step.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}

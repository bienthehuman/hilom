using Hilom.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Infrastructure.Seeding;

public static class DataSeeding
{
    /// <summary>
    /// Runs every registered seeder in order, in its own scope. Seeding connects as the schema owner, like migrations:
    /// the demo generator creates its marker table.
    /// </summary>
    public static async Task RunSeedersAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateOwnerScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }
}

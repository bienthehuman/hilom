using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Seeding;

/// <summary>
/// Creates the System Administrator role and any default role that doesn't exist yet (matched by name).
/// Existing roles are left alone, so admins' edits survive restarts. If no role is the portal role yet, the
/// default portal role (matched by name) becomes it, which also covers databases seeded before slice 2.3.
/// </summary>
internal sealed class RoleSeeder(AppDbContext db) : IDataSeeder
{
    public int Order => 100;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!await db.Set<Role>().AnyAsync(r => r.IsSystem, cancellationToken))
        {
            db.Add(Role.CreateSystemAdministrator());
        }

        var existing = await db.Set<Role>().Select(r => r.Name).ToListAsync(cancellationToken);
        var existingNames = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in DefaultRoles.All.Where(r => !existingNames.Contains(r.Name)))
        {
            db.Add(Role.Create(role.Name, role.Description, role.Grants).Value);
        }

        if (!await db.Set<Role>().AnyAsync(r => r.IsPortal, cancellationToken))
        {
            var portalName = DefaultRoles.All.Single(r => r.IsPortal).Name;
            var portal = db.ChangeTracker.Entries<Role>().Select(e => e.Entity).SingleOrDefault(r => r.Name == portalName)
                ?? await db.Set<Role>().SingleOrDefaultAsync(r => r.Name == portalName, cancellationToken);
            portal?.MarkAsPortalRole();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

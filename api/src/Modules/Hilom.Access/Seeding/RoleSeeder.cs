using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Seeding;

/// <summary>
/// Creates the System Administrator role and any default role that doesn't exist yet (matched by name).
/// Existing roles are left alone, so admins' edits survive restarts.
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

        await db.SaveChangesAsync(cancellationToken);
    }
}

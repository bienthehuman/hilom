using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hilom.Access.Seeding;

/// <summary>
/// Makes sure someone can administer the system:
/// creates the first user from <c>Access:SeedAdmin</c> when no users exist, and gives that account the
/// System Administrator role whenever no active user holds it.
/// </summary>
internal sealed partial class SystemAdministratorSeeder(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    IOptions<AccessOptions> options,
    TimeProvider time,
    ILogger<SystemAdministratorSeeder> logger) : IDataSeeder
{
    public int Order => 110;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value.SeedAdmin;
        var systemRole = await db.Set<Role>().SingleAsync(r => r.IsSystem, cancellationToken);

        var hasActiveAdministrator = await db.Set<UserRole>()
            .Where(ur => ur.RoleId == systemRole.Id)
            .Join(db.Set<User>(), ur => ur.UserId, u => u.Id, (_, u) => u)
            .AnyAsync(u => u.Status == UserStatus.Active, cancellationToken);
        if (hasActiveAdministrator)
        {
            return;
        }

        if (seed is null)
        {
            LogNoAdministrator(logger);
            return;
        }

        var admin = await db.Set<User>().SingleOrDefaultAsync(u => u.Email == seed.Email, cancellationToken);
        if (admin is null)
        {
            if (await db.Set<User>().AnyAsync(cancellationToken))
            {
                LogNoAdministrator(logger);
                return;
            }

            admin = new User(seed.Email, seed.DisplayName);
            admin.ChangePassword(hasher.HashPassword(admin, seed.Password));
            db.Add(admin);
        }

        var alreadyHolds = await db.Set<UserRole>()
            .AnyAsync(ur => ur.UserId == admin.Id && ur.RoleId == systemRole.Id, cancellationToken);
        if (!alreadyHolds)
        {
            db.Add(new UserRole(admin.Id, systemRole.Id, assignedBy: null, time.GetUtcNow()));
            admin.PermissionsChanged();
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, admin.Email);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No active System Administrator exists and Access:SeedAdmin can't provide one. Nobody can manage access.")]
    private static partial void LogNoAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Email} is a System Administrator.")]
    private static partial void LogSeeded(ILogger logger, string email);
}

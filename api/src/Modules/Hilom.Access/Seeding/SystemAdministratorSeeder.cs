using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hilom.Access.Seeding;

/// <summary>
/// Creates the first System Administrator from <c>Access:SeedAdmin</c>, only when no users exist yet.
/// The System Administrator role is attached in slice 0.7.
/// </summary>
internal sealed partial class SystemAdministratorSeeder(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    IOptions<AccessOptions> options,
    ILogger<SystemAdministratorSeeder> logger) : IDataSeeder
{
    public int Order => 100;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<User>().AnyAsync(cancellationToken))
        {
            return;
        }

        var seed = options.Value.SeedAdmin;
        if (seed is null)
        {
            LogNoSeedAdmin(logger);
            return;
        }

        var admin = new User(seed.Email, seed.DisplayName);
        admin.ChangePassword(hasher.HashPassword(admin, seed.Password));
        db.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, admin.Email);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No users exist and Access:SeedAdmin is not set, so nobody can sign in.")]
    private static partial void LogNoSeedAdmin(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the first System Administrator: {Email}")]
    private static partial void LogSeeded(ILogger logger, string email);
}

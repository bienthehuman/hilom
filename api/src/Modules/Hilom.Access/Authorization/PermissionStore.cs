using Hilom.Access.Domain;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Hilom.Access.Authorization;

/// <summary>
/// A user's permissions across all their roles, cached per user and per <see cref="User.PermissionsVersion"/>.
/// Any role change bumps the version, so the next request loads fresh permissions; no waiting for expiry.
/// </summary>
internal sealed partial class PermissionStore(AppDbContext db, IMemoryCache cache, ILogger<PermissionStore> logger)
    : IPermissionStore
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public async Task<PermissionSet> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var version = await db.Set<User>()
            .Where(u => u.Id == userId && u.Status == UserStatus.Active)
            .Select(u => (int?)u.PermissionsVersion)
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return PermissionSet.Empty;
        }

        var key = $"hilom:permissions:{userId}:{version}";
        if (cache.TryGetValue(key, out PermissionSet? cached) && cached is not null)
        {
            return cached;
        }

        var permissions = await LoadAsync(userId, cancellationToken);
        cache.Set(key, permissions, new MemoryCacheEntryOptions { SlidingExpiration = CacheFor });
        return permissions;
    }

    private async Task<PermissionSet> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await db.Set<Role>()
            .Where(r => db.Set<UserRole>().Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .ToListAsync(cancellationToken);

        var grants = new Dictionary<string, HashSet<PermissionScope>>();
        foreach (var grant in roles.SelectMany(r => r.EffectiveGrants()))
        {
            // Rows that no longer match the catalog (renamed permission, narrowed scopes) are ignored, never trusted.
            var permission = Perm.All.FirstOrDefault(p => p.Key == grant.Permission);
            if (permission is null || !permission.Allows(grant.Scope))
            {
                LogIgnoredGrant(logger, grant.Permission, grant.Scope);
                continue;
            }

            if (!grants.TryGetValue(grant.Permission, out var scopes))
            {
                grants[grant.Permission] = scopes = [];
            }

            scopes.Add(grant.Scope);
        }

        return new PermissionSet(grants.ToDictionary(g => g.Key, g => (IReadOnlySet<PermissionScope>)g.Value));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring grant {Permission} ({Scope}): not in the permission catalog or scope not allowed.")]
    private static partial void LogIgnoredGrant(ILogger logger, string permission, PermissionScope scope);
}

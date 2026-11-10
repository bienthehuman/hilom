using Hilom.Access.Domain;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features;

/// <summary>
/// Rules that protect the System Administrator role (docs/RBAC.md, "Guardrails"):
/// - changing who is an administrator (roles, disabling, enabling) needs <c>access.roles.manage</c>;
/// - the last active administrator can't lose the role or be disabled.
/// Callers hold <see cref="LockAsync"/> inside a transaction while checking and changing.
/// </summary>
internal static class SystemAdministratorGuard
{
    // Serializes changes that could remove an administrator, so two admins can't remove each other at once.
    private const long LockKey = 0x48494C4F4D01;

    public static Task LockAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey})", cancellationToken);

    public static Task<Guid> SystemRoleIdAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Set<Role>().Where(r => r.IsSystem).Select(r => r.Id).SingleAsync(cancellationToken);

    public static Task<bool> HoldsAsync(AppDbContext db, Guid userId, Guid systemRoleId, CancellationToken cancellationToken) =>
        db.Set<UserRole>().AnyAsync(ur => ur.UserId == userId && ur.RoleId == systemRoleId, cancellationToken);

    public static Task<bool> OtherActiveAdministratorExistsAsync(
        AppDbContext db,
        Guid userId,
        Guid systemRoleId,
        CancellationToken cancellationToken) =>
        db.Set<UserRole>()
            .Where(ur => ur.RoleId == systemRoleId && ur.UserId != userId)
            .Join(db.Set<User>(), ur => ur.UserId, u => u.Id, (_, u) => u)
            .AnyAsync(u => u.Status == UserStatus.Active, cancellationToken);

    /// <summary>Fails unless the current user may change who is a System Administrator.</summary>
    public static async Task<Error?> EnsureMayChangeAdministratorsAsync(
        ICurrentPermissions permissions,
        CancellationToken cancellationToken) =>
        (await permissions.GetAsync(cancellationToken)).Has(Perm.Access.RolesManage)
            ? null
            : AccessErrors.SystemAdministratorChangeNeedsRolesManage;
}

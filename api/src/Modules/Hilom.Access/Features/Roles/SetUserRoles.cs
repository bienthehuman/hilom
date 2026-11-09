using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Roles;

/// <summary>
/// Replaces a user's roles. Guardrails (docs/RBAC.md): nobody changes their own roles, and the last
/// active System Administrator can't lose the role.
/// </summary>
internal static class SetUserRoles
{
    // Serializes role changes that could remove a System Administrator, so two admins can't
    // remove each other at the same moment and leave nobody in charge.
    private const long SystemAdministratorLock = 0x48494C4F4D01;

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> Handle(
        Guid userId,
        SetUserRolesRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId == userId)
        {
            return AccessErrors.CannotChangeOwnRoles.ToProblemResult();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SystemAdministratorLock})", cancellationToken);

        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AccessErrors.UserNotFound.ToProblemResult();
        }

        var requested = request.RoleIds.Distinct().ToList();
        var roles = await db.Set<Role>().Where(r => requested.Contains(r.Id)).ToListAsync(cancellationToken);
        if (roles.Count != requested.Count)
        {
            return AccessErrors.UnknownRoles.ToValidationResult();
        }

        var current = await db.Set<UserRole>().Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
        var systemRoleId = await db.Set<Role>().Where(r => r.IsSystem).Select(r => r.Id).SingleAsync(cancellationToken);

        var losesSystemRole = current.Any(ur => ur.RoleId == systemRoleId) && !requested.Contains(systemRoleId);
        if (losesSystemRole && user.Status == UserStatus.Active && !await OtherActiveAdministratorExistsAsync())
        {
            return AccessErrors.LastSystemAdministrator.ToProblemResult();
        }

        db.RemoveRange(current.Where(ur => !requested.Contains(ur.RoleId)));
        var now = time.GetUtcNow();
        foreach (var roleId in requested.Where(id => current.All(ur => ur.RoleId != id)))
        {
            db.Add(new UserRole(userId, roleId, currentUser.UserId, now));
        }

        user.PermissionsChanged();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();

        Task<bool> OtherActiveAdministratorExistsAsync() =>
            db.Set<UserRole>()
                .Where(ur => ur.RoleId == systemRoleId && ur.UserId != userId)
                .Join(db.Set<User>(), ur => ur.UserId, u => u.Id, (_, u) => u)
                .AnyAsync(u => u.Status == UserStatus.Active, cancellationToken);
    }
}

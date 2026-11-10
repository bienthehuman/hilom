using Hilom.Access.Domain;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Roles;

/// <summary>
/// Replaces a user's roles. Guardrails (docs/RBAC.md): nobody changes their own roles, only someone who can
/// manage roles grants or removes System Administrator, and the last active administrator keeps the role.
/// </summary>
internal static class SetUserRoles
{
    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> Handle(
        Guid userId,
        SetUserRolesRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId == userId)
        {
            return AccessErrors.CannotChangeOwnRoles.ToProblemResult();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await SystemAdministratorGuard.LockAsync(db, cancellationToken);

        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AccessErrors.UserNotFound.ToProblemResult();
        }

        var requested = request.RoleIds.Distinct().ToList();
        var found = await db.Set<Role>().CountAsync(r => requested.Contains(r.Id), cancellationToken);
        if (found != requested.Count)
        {
            return AccessErrors.UnknownRoles.ToValidationResult();
        }

        var current = await db.Set<UserRole>().Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
        var systemRoleId = await SystemAdministratorGuard.SystemRoleIdAsync(db, cancellationToken);
        var holdsNow = current.Any(ur => ur.RoleId == systemRoleId);
        var holdsAfter = requested.Contains(systemRoleId);

        if (holdsNow != holdsAfter)
        {
            if (await SystemAdministratorGuard.EnsureMayChangeAdministratorsAsync(permissions, cancellationToken) is { } denied)
            {
                return denied.ToProblemResult();
            }

            if (holdsNow
                && user.Status == UserStatus.Active
                && !await SystemAdministratorGuard.OtherActiveAdministratorExistsAsync(db, userId, systemRoleId, cancellationToken))
            {
                return AccessErrors.LastSystemAdministrator.ToProblemResult();
            }
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
    }
}

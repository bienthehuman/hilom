using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Roles;

/// <summary>
/// Replaces a user's roles. Guardrails (docs/RBAC.md): nobody changes their own roles, only someone who can
/// manage roles grants or removes System Administrator, the last active administrator keeps the role, and
/// portal and staff roles never mix.
/// </summary>
internal static class SetUserRoles
{
    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> Handle(
        Guid userId,
        SetUserRolesRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
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

        if (await PortalRoleGuard.CheckAsync(db, user.AccountType, requested, cancellationToken) is { } mixed)
        {
            return mixed.ToProblemResult();
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

        var currentIds = current.Select(ur => ur.RoleId).ToList();
        var roleNames = await db.Set<Role>()
            .Where(r => requested.Contains(r.Id) || currentIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        List<string> NamesOf(IEnumerable<Guid> ids) => [.. ids.Select(id => roleNames[id]).Order(StringComparer.Ordinal)];
        var before = NamesOf(currentIds);
        var after = NamesOf(requested);
        if (before.SequenceEqual(after))
        {
            auditLog.NothingToRecord();
        }
        else
        {
            auditLog.Record(
                AuditEvents.Access.UserRolesChanged,
                new AuditOptions
                {
                    Target = new AuditTarget(nameof(User), userId),
                    Details = new { user = user.Email, before, after },
                });
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

using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Users;

/// <summary>User accounts: list, view, create, disable, enable, unlock. Roles are set via SetUserRoles.</summary>
internal static class UserEndpoints
{
    private static readonly string[] SortFields = ["name", "email", "lastLoginAt", "createdAt"];

    public static async Task<Ok<PagedResponse<UserResponse>>> ListUsers(
        AppDbContext db,
        TimeProvider time,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        UserStatusFilter? status,
        AccountType? accountType,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var (field, descending) = Paging.ParseSort(sort, SortFields, "name");
        var now = time.GetUtcNow();

        var query = db.Set<User>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.DisplayName, pattern) || EF.Functions.ILike(u.Email, pattern));
        }

        if (accountType is { } type)
        {
            query = query.Where(u => u.AccountType == type);
        }

        query = status switch
        {
            UserStatusFilter.Active => query.Where(u => u.Status == UserStatus.Active),
            UserStatusFilter.Disabled => query.Where(u => u.Status == UserStatus.Disabled),
            UserStatusFilter.Locked => query.Where(u => u.LockoutEndsAt > now),
            _ => query,
        };

        query = (field, descending) switch
        {
            ("email", false) => query.OrderBy(u => u.Email),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("lastLoginAt", false) => query.OrderBy(u => u.LastLoginAt),
            ("lastLoginAt", true) => query.OrderByDescending(u => u.LastLoginAt),
            ("createdAt", false) => query.OrderBy(u => u.CreatedAt),
            ("createdAt", true) => query.OrderByDescending(u => u.CreatedAt),
            (_, true) => query.OrderByDescending(u => u.DisplayName),
            _ => query.OrderBy(u => u.DisplayName),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(db, query.Skip((pageNumber - 1) * size).Take(size), now).ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedResponse<UserResponse>(items, total, pageNumber, size));
    }

    public static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetUser(
        Guid id,
        AppDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var user = await Project(db, db.Set<User>().AsNoTracking().Where(u => u.Id == id), time.GetUtcNow())
            .SingleOrDefaultAsync(cancellationToken);
        return user is null ? AccessErrors.UserNotFound.ToProblemResult() : TypedResults.Ok(user);
    }

    public static async Task<Results<Created<UserResponse>, ProblemHttpResult, ValidationProblem>> CreateUser(
        CreateUserRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await db.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return AccessErrors.EmailTaken.ToProblemResult();
        }

        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        if (await db.Set<Role>().CountAsync(r => roleIds.Contains(r.Id), cancellationToken) != roleIds.Count)
        {
            return AccessErrors.UnknownRoles.ToValidationResult();
        }

        // Administrators create staff accounts; portal accounts register themselves.
        if (await PortalRoleGuard.CheckAsync(db, AccountType.Staff, roleIds, cancellationToken) is { } portal)
        {
            return portal.ToProblemResult();
        }

        var systemRoleId = await SystemAdministratorGuard.SystemRoleIdAsync(db, cancellationToken);
        if (roleIds.Contains(systemRoleId)
            && await SystemAdministratorGuard.EnsureMayChangeAdministratorsAsync(permissions, cancellationToken) is { } denied)
        {
            return denied.ToProblemResult();
        }

        var user = new User(email, request.DisplayName);
        user.SetTemporaryPassword(hasher.HashPassword(user, request.Password));
        db.Add(user);

        var now = time.GetUtcNow();
        foreach (var roleId in roleIds)
        {
            db.Add(new UserRole(user.Id, roleId, currentUser.UserId, now));
        }

        var roleNames = await db.Set<Role>().Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(cancellationToken);
        auditLog.Record(
            AuditEvents.Access.UserCreated,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(User), user.Id),
                Details = new { email = user.Email, displayName = user.DisplayName, roles = roleNames.Order(StringComparer.Ordinal) },
            });
        await db.SaveChangesAsync(cancellationToken);

        var created = await Project(db, db.Set<User>().AsNoTracking().Where(u => u.Id == user.Id), now)
            .SingleAsync(cancellationToken);
        return TypedResults.Created($"/api/access/users/{user.Id}", created);
    }

    public static Task<Results<NoContent, ProblemHttpResult>> DisableUser(
        Guid id,
        AppDbContext db,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, disable: true, db, currentUser, permissions, auditLog, cancellationToken);

    public static Task<Results<NoContent, ProblemHttpResult>> EnableUser(
        Guid id,
        AppDbContext db,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, disable: false, db, currentUser, permissions, auditLog, cancellationToken);

    public static async Task<Results<NoContent, ProblemHttpResult>> UnlockUser(
        Guid id,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AccessErrors.UserNotFound.ToProblemResult();
        }

        user.Unlock();
        auditLog.Record(
            AuditEvents.Access.UserUnlocked,
            new AuditOptions { Target = new AuditTarget(nameof(User), user.Id), Details = new { email = user.Email } });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangeStatusAsync(
        Guid id,
        bool disable,
        AppDbContext db,
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        if (disable && currentUser.UserId == id)
        {
            return AccessErrors.CannotDisableSelf.ToProblemResult();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await SystemAdministratorGuard.LockAsync(db, cancellationToken);

        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AccessErrors.UserNotFound.ToProblemResult();
        }

        var systemRoleId = await SystemAdministratorGuard.SystemRoleIdAsync(db, cancellationToken);
        if (await SystemAdministratorGuard.HoldsAsync(db, id, systemRoleId, cancellationToken))
        {
            if (await SystemAdministratorGuard.EnsureMayChangeAdministratorsAsync(permissions, cancellationToken) is { } denied)
            {
                return denied.ToProblemResult();
            }

            if (disable
                && user.Status == UserStatus.Active
                && !await SystemAdministratorGuard.OtherActiveAdministratorExistsAsync(db, id, systemRoleId, cancellationToken))
            {
                return AccessErrors.LastSystemAdministrator.ToProblemResult();
            }
        }

        if (disable)
        {
            user.Disable();
        }
        else
        {
            user.Enable();
        }

        auditLog.Record(
            disable ? AuditEvents.Access.UserDisabled : AuditEvents.Access.UserEnabled,
            new AuditOptions { Target = new AuditTarget(nameof(User), user.Id), Details = new { email = user.Email } });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static IQueryable<UserResponse> Project(AppDbContext db, IQueryable<User> users, DateTimeOffset now) =>
        users.Select(u => new UserResponse(
            u.Id,
            u.Email,
            u.DisplayName,
            u.AccountType,
            u.Status,
            u.LockoutEndsAt > now,
            u.LockoutEndsAt > now ? u.LockoutEndsAt : null,
            u.LastLoginAt,
            u.CreatedAt,
            db.Set<UserRole>()
                .Where(ur => ur.UserId == u.Id)
                .Join(db.Set<Role>(), ur => ur.RoleId, r => r.Id, (_, r) => r)
                .OrderByDescending(r => r.IsSystem)
                .ThenBy(r => r.Name)
                .Select(r => new RoleRef(r.Id, r.Name, r.IsSystem, r.IsPortal))
                .ToList()));
}

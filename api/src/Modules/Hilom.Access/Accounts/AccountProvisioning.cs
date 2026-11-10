using Hilom.Access.Domain;
using Hilom.Access.Features;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Accounts;

/// <param name="RoleIds">Roles to grant. Ones deleted since the request was made are skipped.</param>
/// <param name="AssignedBy">Who granted the roles (e.g. the person who sent the invite).</param>
public sealed record NewAccount(string Email, string DisplayName, string Password, IReadOnlyCollection<Guid> RoleIds, Guid? AssignedBy);

/// <param name="GrantedRoles">Names of the roles actually granted.</param>
public sealed record CreatedAccount(Guid UserId, IReadOnlyList<string> GrantedRoles);

/// <summary>
/// How other modules create sign-in accounts (e.g. Workforce, when a staff invite is accepted). Account rules
/// stay in Access: unique emails, password hashing, and who may hand out the System Administrator role.
/// </summary>
public interface IAccountProvisioning
{
    Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Checks that the current user may promise these roles to someone: they must exist, and the System
    /// Administrator role needs <c>access.roles.manage</c> (docs/RBAC.md, "Guardrails").
    /// </summary>
    Task<Error?> CheckGrantableAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleName>> RoleNamesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>Adds the user and their roles to the unit of work. The caller saves, in its own transaction.</summary>
    Task<Result<CreatedAccount>> CreateAsync(NewAccount account, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record RoleName(Guid Id, string Name);

internal sealed class AccountProvisioning(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    ICurrentPermissions permissions) : IAccountProvisioning
{
    public Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken)
    {
        var trimmed = email.Trim();
        return db.Set<User>().AnyAsync(u => u.Email == trimmed, cancellationToken);
    }

    public async Task<Error?> CheckGrantableAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var ids = roleIds.Distinct().ToList();
        if (await db.Set<Role>().CountAsync(r => ids.Contains(r.Id), cancellationToken) != ids.Count)
        {
            return AccessErrors.UnknownRoles;
        }

        var systemRoleId = await SystemAdministratorGuard.SystemRoleIdAsync(db, cancellationToken);
        return ids.Contains(systemRoleId)
            ? await SystemAdministratorGuard.EnsureMayChangeAdministratorsAsync(permissions, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<RoleName>> RoleNamesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        await db.Set<Role>()
            .Where(r => roleIds.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => new RoleName(r.Id, r.Name))
            .ToListAsync(cancellationToken);

    public async Task<Result<CreatedAccount>> CreateAsync(NewAccount account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (await EmailInUseAsync(account.Email, cancellationToken))
        {
            return Result.Failure<CreatedAccount>(AccessErrors.EmailTaken);
        }

        var user = new User(account.Email.Trim(), account.DisplayName.Trim());
        user.ChangePassword(hasher.HashPassword(user, account.Password));
        db.Add(user);

        var roles = await RoleNamesAsync(account.RoleIds, cancellationToken);
        foreach (var role in roles)
        {
            db.Add(new UserRole(user.Id, role.Id, account.AssignedBy, now));
        }

        return Result.Success(new CreatedAccount(user.Id, [.. roles.Select(r => r.Name)]));
    }
}

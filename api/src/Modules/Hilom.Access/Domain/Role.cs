using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Access.Domain;

/// <summary>A named bundle of permissions, each with a scope. Admins build roles in the UI (docs/RBAC.md).</summary>
public sealed class Role : Entity
{
    public const string SystemAdministratorName = "System Administrator";

    private readonly List<RoleGrant> _grants = [];

    private Role()
    {
    }

    private Role(string name, string description, bool isSystem)
    {
        Name = name.Trim();
        Description = description.Trim();
        IsSystem = isSystem;
    }

    public string Name { get; private set; } = "";

    public string Description { get; private set; } = "";

    /// <summary>
    /// Only the System Administrator role. It can't be edited or deleted, and it always holds every
    /// permission that allows the <c>all</c> scope, including permissions added later.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// The role new patient portal accounts get when they register (one role at most). Portal accounts hold
    /// only this role, and staff accounts never hold it. It can be edited but not deleted.
    /// </summary>
    public bool IsPortal { get; private set; }

    public IReadOnlyCollection<RoleGrant> Grants => _grants;

    /// <summary>Makes this the role self-registered portal accounts get. Done by seeding.</summary>
    public void MarkAsPortalRole() => IsPortal = true;

    public static Role CreateSystemAdministrator() =>
        new(SystemAdministratorName, "Full access. Can't be edited or deleted.", isSystem: true);

    public static Result<Role> Create(string name, string description, IEnumerable<RoleGrant> grants)
    {
        var role = new Role(name, description, isSystem: false);
        var result = role.SetGrants(grants);
        return result.IsSuccess ? Result.Success(role) : Result.Failure<Role>(result.Error);
    }

    public Result Rename(string name, string description)
    {
        if (IsSystem)
        {
            return Result.Failure(AccessErrors.SystemRoleProtected);
        }

        Name = name.Trim();
        Description = description.Trim();
        return Result.Success();
    }

    /// <summary>Replaces every grant. Each permission may appear once, with a scope its catalog entry allows.</summary>
    public Result SetGrants(IEnumerable<RoleGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (IsSystem)
        {
            return Result.Failure(AccessErrors.SystemRoleProtected);
        }

        var list = grants.ToList();
        var problems = new Dictionary<string, string[]>();

        foreach (var duplicate in list.GroupBy(g => g.Permission).Where(g => g.Count() > 1))
        {
            problems[duplicate.Key] = ["This permission is listed more than once."];
        }

        foreach (var grant in list)
        {
            var permission = Perm.All.FirstOrDefault(p => p.Key == grant.Permission);
            if (permission is null)
            {
                problems[grant.Permission] = ["This permission doesn't exist."];
            }
            else if (!permission.Allows(grant.Scope))
            {
                var allowed = string.Join(", ", permission.AllowedScopes.Select(s => s.ToString().ToLowerInvariant()));
                problems[grant.Permission] = [$"This permission can only be granted with: {allowed}."];
            }
        }

        if (problems.Count > 0)
        {
            return Result.Failure(Error.Validation("invalid_grants", "Some permissions can't be granted as chosen.", problems));
        }

        _grants.Clear();
        _grants.AddRange(list.Select(g => new RoleGrant(g.Permission, g.Scope)));
        return Result.Success();
    }

    /// <summary>What this role actually grants. For the system role, computed from the catalog.</summary>
    public IReadOnlyList<RoleGrant> EffectiveGrants() =>
        IsSystem
            ? [.. Perm.All.Where(p => p.Allows(PermissionScope.All)).Select(p => new RoleGrant(p.Key, PermissionScope.All))]
            : [.. _grants];
}

/// <summary>One permission granted by a role, at one scope.</summary>
public sealed record RoleGrant(string Permission, PermissionScope Scope);

/// <summary>A user holding a role.</summary>
public sealed class UserRole
{
    private UserRole()
    {
    }

    public UserRole(Guid userId, Guid roleId, Guid? assignedBy, DateTimeOffset assignedAt)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedBy = assignedBy;
        AssignedAt = assignedAt;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid? AssignedBy { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
}

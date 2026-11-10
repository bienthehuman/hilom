namespace Hilom.SharedKernel.Authorization;

/// <summary>
/// Which records one user may reach with one permission (docs/RBAC.md, "Scopes"). Modules turn this into a
/// query filter for their own tables, e.g. "own" means the employee record linked to <see cref="UserId"/>.
/// </summary>
public sealed record AccessScope(Guid? UserId, IReadOnlyList<PermissionScope> Scopes)
{
    public static AccessScope None { get; } = new(null, []);

    /// <summary>Every record; no filter needed.</summary>
    public bool IsAll => Scopes.Contains(PermissionScope.All);

    public bool Includes(PermissionScope scope) => IsAll || Scopes.Contains(scope);

    /// <summary>The permission isn't granted at all (the endpoint's permission check normally prevents this).</summary>
    public bool IsEmpty => Scopes.Count == 0;
}

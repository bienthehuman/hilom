namespace Hilom.SharedKernel.Authorization;

/// <summary>
/// Which records a permission reaches (docs/RBAC.md, "Scopes"). Declared widest first.
/// </summary>
public enum PermissionScope
{
    /// <summary>Every record.</summary>
    All,

    /// <summary>Records tied to a ward the user is assigned to today.</summary>
    Ward,

    /// <summary>Records the user is directly responsible for (their patients, their own record).</summary>
    Own,

    /// <summary>Patients the user has an active, verified account link to.</summary>
    Linked,
}

/// <summary>An action someone can be allowed to do, e.g. <c>clinical.vitals.record</c>.</summary>
/// <param name="Key">Stable identifier stored in role grants. Never rename once released.</param>
/// <param name="Group">Module it belongs to, for grouping in the role editor.</param>
/// <param name="Description">What it allows, in plain words.</param>
/// <param name="AllowedScopes">Scopes a role may grant it with. The code enforces each one.</param>
public sealed record Permission(string Key, string Group, string Description, IReadOnlyList<PermissionScope> AllowedScopes)
{
    public bool Allows(PermissionScope scope) => AllowedScopes.Contains(scope);

    public override string ToString() => Key;
}

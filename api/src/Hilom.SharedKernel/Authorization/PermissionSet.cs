using System.Collections.Frozen;

namespace Hilom.SharedKernel.Authorization;

/// <summary>
/// Everything one user may do: permission → the scopes granted across all their roles.
/// Several roles combine as a union; holding <see cref="PermissionScope.All"/> makes the others irrelevant.
/// </summary>
public sealed class PermissionSet
{
    public static readonly PermissionSet Empty = new(new Dictionary<string, IReadOnlySet<PermissionScope>>());

    private readonly FrozenDictionary<string, IReadOnlySet<PermissionScope>> _grants;

    public PermissionSet(IReadOnlyDictionary<string, IReadOnlySet<PermissionScope>> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        _grants = grants
            .Where(pair => pair.Value.Count > 0)
            .ToFrozenDictionary(
                pair => pair.Key,
                pair => pair.Value.Contains(PermissionScope.All)
                    ? (IReadOnlySet<PermissionScope>)new HashSet<PermissionScope> { PermissionScope.All }
                    : pair.Value);
    }

    public IEnumerable<string> Keys => _grants.Keys;

    public bool Has(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return _grants.ContainsKey(permission.Key);
    }

    /// <summary>Scopes granted for the permission, widest first. Empty when not granted.</summary>
    public IReadOnlyList<PermissionScope> ScopesFor(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return ScopesFor(permission.Key);
    }

    public IReadOnlyList<PermissionScope> ScopesFor(string key) =>
        _grants.TryGetValue(key, out var scopes) ? [.. scopes.Order()] : [];

    public bool HasAll(Permission permission) => ScopesFor(permission).Contains(PermissionScope.All);
}

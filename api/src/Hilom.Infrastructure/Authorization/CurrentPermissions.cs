using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Security;

namespace Hilom.Infrastructure.Authorization;

/// <summary>
/// The signed-in user's permissions, for handlers that must check a record against a scope
/// (e.g. "may this nurse see this patient?"). Loaded once per request.
/// </summary>
public interface ICurrentPermissions
{
    Task<PermissionSet> GetAsync(CancellationToken cancellationToken);

    /// <summary>The records the user may reach with <paramref name="permission"/>.</summary>
    Task<AccessScope> ScopeForAsync(Permission permission, CancellationToken cancellationToken);
}

internal sealed class CurrentPermissions(ICurrentUser currentUser, IPermissionStore store) : ICurrentPermissions
{
    private PermissionSet? _loaded;

    public async Task<PermissionSet> GetAsync(CancellationToken cancellationToken)
    {
        if (_loaded is not null)
        {
            return _loaded;
        }

        _loaded = currentUser.UserId is { } userId
            ? await store.GetPermissionsAsync(userId, cancellationToken)
            : PermissionSet.Empty;
        return _loaded;
    }

    public async Task<AccessScope> ScopeForAsync(Permission permission, CancellationToken cancellationToken)
    {
        var permissions = await GetAsync(cancellationToken);
        var scopes = permissions.ScopesFor(permission);
        return scopes.Count == 0 ? AccessScope.None : new AccessScope(currentUser.UserId, scopes);
    }
}

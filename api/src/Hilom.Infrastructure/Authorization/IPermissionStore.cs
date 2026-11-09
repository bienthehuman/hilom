using Hilom.SharedKernel.Authorization;

namespace Hilom.Infrastructure.Authorization;

/// <summary>Loads what a user may do. Implemented (and cached) by the Access module.</summary>
public interface IPermissionStore
{
    Task<PermissionSet> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);
}

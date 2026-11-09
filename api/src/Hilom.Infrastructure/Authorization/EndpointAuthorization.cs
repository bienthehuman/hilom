using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Hilom.Infrastructure.Authorization;

/// <summary>Marks an endpoint as protected by a permission. Read by the endpoint-coverage test.</summary>
public sealed record RequiredPermissionMetadata(Permission Permission);

/// <summary>Marks an endpoint that only needs a signed-in user, with no permission (e.g. "who am I").</summary>
public sealed record SignedInOnlyMetadata;

public static class EndpointAuthorization
{
    /// <summary>
    /// Only users whose roles grant <paramref name="permission"/> (at any scope) reach the handler; others get 403.
    /// The handler still checks the record against the user's scope (docs/RBAC.md, "Enforcement").
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(permission);
        return builder
            .WithMetadata(new RequiredPermissionMetadata(permission))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + permission.Key);
    }

    /// <summary>Any signed-in user may call this endpoint. Use sparingly; most endpoints need a permission.</summary>
    public static TBuilder RequireSignedIn<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SignedInOnlyMetadata()).RequireAuthorization();
}

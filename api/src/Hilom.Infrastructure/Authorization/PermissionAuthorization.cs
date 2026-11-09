using System.Security.Claims;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Hilom.Infrastructure.Authorization;

/// <summary>Requirement: the user holds <see cref="PermissionKey"/> at any scope.</summary>
public sealed record PermissionRequirement(string PermissionKey) : IAuthorizationRequirement;

/// <summary>
/// Turns policy names like <c>perm:clinical.vitals.record</c> into permission requirements, so every
/// permission is a policy without registering 43 policies by hand.
/// </summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var key = policyName[Prefix.Length..];
        if (!Perm.All.Any(p => p.Key == key))
        {
            throw new InvalidOperationException($"Unknown permission '{key}'. Add it to the catalog in Perm.cs.");
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(key))
            .Build();
    }
}

/// <summary>Grants access when the signed-in user's roles include the permission.</summary>
internal sealed class PermissionAuthorizationHandler(IPermissionStore store) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        var cancellationToken = (context.Resource as Microsoft.AspNetCore.Http.HttpContext)?.RequestAborted ?? default;
        var permissions = await store.GetPermissionsAsync(userId, cancellationToken);
        if (permissions.ScopesFor(requirement.PermissionKey).Count > 0)
        {
            context.Succeed(requirement);
        }
    }
}

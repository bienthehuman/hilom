using Hilom.Infrastructure.Authorization;
using Hilom.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.IntegrationTests.Access;

/// <summary>
/// Every API endpoint must say who may call it: a permission, "any signed-in user", or anonymous.
/// This test is what stops a new endpoint from shipping unprotected (docs/RBAC.md, "Tests").
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class EndpointCoverageTests(HilomApiFactory factory)
{
    [Fact]
    public void Every_api_endpoint_declares_exactly_one_access_rule()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty();

        var problems = endpoints
            .Select(e => new
            {
                Route = $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}",
                Rules = new[]
                {
                    e.Metadata.GetMetadata<RequiredPermissionMetadata>() is not null,
                    e.Metadata.GetMetadata<SignedInOnlyMetadata>() is not null,
                    e.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                }.Count(declared => declared),
            })
            .Where(e => e.Rules != 1)
            .Select(e => $"{e.Route} declares {e.Rules} access rules")
            .ToList();

        problems.ShouldBeEmpty(
            "Use .RequirePermission(Perm.X), .RequireSignedIn() or .AllowAnonymous() — exactly one per endpoint.");
    }
}

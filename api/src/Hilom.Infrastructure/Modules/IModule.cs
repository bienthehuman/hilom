using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Infrastructure.Modules;

/// <summary>
/// What every module provides to the host. The host lists the modules; Infrastructure never references them.
/// </summary>
public interface IModule
{
    /// <summary>Display name, also used as the OpenAPI tag.</summary>
    string Name { get; }

    /// <summary>PostgreSQL schema that holds this module's tables.</summary>
    string Schema { get; }

    /// <summary>Route segment under <c>/api</c>.</summary>
    string RoutePrefix => Schema;

    /// <summary>Assembly scanned for EF entity configurations.</summary>
    Assembly Assembly => GetType().Assembly;

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

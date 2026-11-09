using Hilom.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Access;

/// <summary>Users, roles, permissions and the audit log.</summary>
public sealed class AccessModule : IModule
{
    public string Name => "Access";

    public string Schema => "access";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

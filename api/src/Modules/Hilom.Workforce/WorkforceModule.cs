using Hilom.Infrastructure.Modules;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Features.Lookups;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Workforce;

/// <summary>Employees, physicians, invites, shifts, ward assignments, roster and swaps.</summary>
public sealed class WorkforceModule : IModule
{
    public string Name => "Workforce";

    public string Schema => "workforce";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapLookup<Department>("departments");
        endpoints.MapLookup<Position>("positions");
        endpoints.MapLookup<Specialization>("specializations");
    }
}

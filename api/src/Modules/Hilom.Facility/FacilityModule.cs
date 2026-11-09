using Hilom.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Facility;

/// <summary>Wards and beds.</summary>
public sealed class FacilityModule : IModule
{
    public string Name => "Facility";

    public string Schema => "facility";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

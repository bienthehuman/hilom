using Hilom.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Clinical;

/// <summary>Encounters, admissions, bed assignments, vitals, notes, orders and medication administration.</summary>
public sealed class ClinicalModule : IModule
{
    public string Name => "Clinical";

    public string Schema => "clinical";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

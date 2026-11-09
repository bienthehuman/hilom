using Hilom.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Scheduling;

/// <summary>Availability slots and appointments.</summary>
public sealed class SchedulingModule : IModule
{
    public string Name => "Scheduling";

    public string Schema => "scheduling";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

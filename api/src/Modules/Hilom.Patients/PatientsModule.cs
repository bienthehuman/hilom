using Hilom.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Patients;

/// <summary>Patient registry, allergies and patient account links.</summary>
public sealed class PatientsModule : IModule
{
    public string Name => "Patients";

    public string Schema => "patients";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

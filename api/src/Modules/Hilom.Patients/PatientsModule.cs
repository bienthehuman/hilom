using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Features.Registry;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Patients;

/// <summary>Patient registry, allergies and patient account links.</summary>
public sealed class PatientsModule : IModule
{
    public const string SchemaName = "patients";

    public string Name => "Patients";

    public string Schema => SchemaName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("", PatientEndpoints.ListPatients)
            .RequirePermission(Perm.Patients.View)
            .WithName("ListPatients")
            .WithSummary("Patients in the caller's scope, paged, searchable by name, MRN and date of birth");
        endpoints.MapGet("/{id:guid}", PatientEndpoints.GetPatient)
            .RequirePermission(Perm.Patients.View)
            .WithName("GetPatient")
            .WithSummary("One patient's demographics; 404 when outside the caller's scope. Logged as a record view.");
        endpoints.MapPost("", PatientEndpoints.RegisterPatient)
            .RequirePermission(Perm.Patients.Register)
            .WithValidation<RegisterPatientRequest>()
            .Audited(AuditEvents.Patients.PatientRegistered)
            .WithName("RegisterPatient")
            .WithSummary("Register a patient; the MRN is generated");
        endpoints.MapPut("/{id:guid}", PatientEndpoints.UpdatePatient)
            .RequirePermission(Perm.Patients.Edit)
            .WithValidation<UpdatePatientRequest>()
            .Audited(AuditEvents.Patients.DemographicsChanged)
            .WithName("UpdatePatient")
            .WithSummary("Edit demographics");
    }
}

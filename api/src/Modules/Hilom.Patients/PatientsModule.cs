using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Features.Allergies;
using Hilom.Patients.Features.Links;
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
        services.AddScoped<PatientReach>();
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

        var links = endpoints.MapGroup("/links");
        links.MapGet("/mine", LinkEndpoints.ListMyLinks)
            .RequirePermission(Perm.Patients.LinksRequest)
            .WithName("ListMyLinks")
            .WithSummary("The portal user's own links and requests");
        links.MapPost("", LinkEndpoints.RequestLink)
            .RequirePermission(Perm.Patients.LinksRequest)
            .WithValidation<RequestLinkRequest>()
            .Audited(AuditEvents.Patients.LinkRequested)
            .WithName("RequestLink")
            .WithSummary("Ask to be linked to a patient, described as the requester knows them");
        links.MapGet("", LinkEndpoints.ListLinks)
            .RequirePermission(Perm.Patients.LinksManage)
            .WithName("ListLinks")
            .WithSummary("Links by status; pending (the review queue, oldest first) by default");
        links.MapPost("/{id:guid}/verify", LinkEndpoints.VerifyLink)
            .RequirePermission(Perm.Patients.LinksManage)
            .WithValidation<VerifyLinkRequest>()
            .Audited(AuditEvents.Patients.LinkVerified)
            .WithName("VerifyLink")
            .WithSummary("Match a pending request to a patient and activate it");
        links.MapPost("/{id:guid}/reject", LinkEndpoints.RejectLink)
            .RequirePermission(Perm.Patients.LinksManage)
            .WithValidation<RejectLinkRequest>()
            .Audited(AuditEvents.Patients.LinkRejected)
            .WithName("RejectLink");
        links.MapPut("/{id:guid}/permissions", LinkEndpoints.SetLinkPermissions)
            .RequirePermission(Perm.Patients.LinksManage)
            .Audited(AuditEvents.Patients.LinkPermissionsChanged)
            .WithName("SetLinkPermissions")
            .WithSummary("Change can_book / can_view_records on an active link");
        links.MapPost("/{id:guid}/revoke", LinkEndpoints.RevokeLink)
            .RequirePermission(Perm.Patients.LinksManage)
            .WithValidation<RevokeLinkRequest>()
            .Audited(AuditEvents.Patients.LinkRevoked)
            .WithName("RevokeLink")
            .WithSummary("End an active link; access ends on the account's next request");
        endpoints.MapGet("/{patientId:guid}/links", LinkEndpoints.ListPatientLinks)
            .RequirePermission(Perm.Patients.LinksManage)
            .WithName("ListPatientLinks")
            .WithSummary("Portal accounts linked to a patient, with history");

        var allergies = endpoints.MapGroup("/{patientId:guid}/allergies");
        allergies.MapGet("", AllergyEndpoints.ListAllergies)
            .RequirePermission(Perm.Patients.View)
            .WithName("ListAllergies")
            .WithSummary("All of a patient's allergy entries: active ones first, most severe first, then history. Logged as a record view.");
        allergies.MapPost("", AllergyEndpoints.RecordAllergy)
            .RequirePermission(Perm.Patients.AllergiesManage)
            .WithValidation<RecordAllergyRequest>()
            .Audited(AuditEvents.Patients.AllergyRecorded)
            .WithName("RecordAllergy");
        allergies.MapPut("/{id:guid}", AllergyEndpoints.UpdateAllergy)
            .RequirePermission(Perm.Patients.AllergiesManage)
            .WithValidation<UpdateAllergyRequest>()
            .Audited(AuditEvents.Patients.AllergyChanged)
            .WithName("UpdateAllergy")
            .WithSummary("Edit an active allergy entry");
        allergies.MapPost("/{id:guid}/status", AllergyEndpoints.ChangeAllergyStatus)
            .RequirePermission(Perm.Patients.AllergiesManage)
            .WithValidation<ChangeAllergyStatusRequest>()
            .Audited(AuditEvents.Patients.AllergyChanged)
            .WithName("ChangeAllergyStatus")
            .WithSummary("Mark an active entry resolved, or entered in error (with a reason)");
    }
}

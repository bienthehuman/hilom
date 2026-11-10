using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Scopes;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Features.Registry;
using Hilom.Scheduling.Visits;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Clinical;

/// <summary>Encounters, admissions, bed assignments, vitals, notes, orders and medication administration.</summary>
public sealed class ClinicalModule : IModule
{
    public const string SchemaName = "clinical";

    public string Name => "Clinical";

    public string Schema => SchemaName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPatientReachSource, OwnPatientsThroughEncounters>();
        services.AddScoped<IAppointmentEncounters, AppointmentEncounters>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var encounters = endpoints.MapGroup("/encounters");
        encounters.MapPost("/check-in", EncounterEndpoints.CheckIn)
            .RequirePermission(Perm.Scheduling.AppointmentsCheckIn)
            .WithValidation<CheckInRequest>()
            .Audited(AuditEvents.Scheduling.AppointmentCheckedIn, AuditEvents.Clinical.EncounterOpened)
            .WithName("CheckIn")
            .WithSummary("Check a patient in for a confirmed appointment today; opens an outpatient encounter");
        encounters.MapGet("", EncounterEndpoints.ListEncounters)
            .RequirePermission(Perm.Clinical.EncountersView)
            .WithName("ListEncounters")
            .WithSummary("Encounters in the caller's reach, newest first, optionally one patient's or by status");
        encounters.MapGet("/{id:guid}", EncounterEndpoints.GetEncounter)
            .RequirePermission(Perm.Clinical.EncountersView)
            .WithName("GetEncounter")
            .WithSummary("One encounter; 404 when outside the caller's scope. Logged as a record view.");
        encounters.MapPut("/{id:guid}", EncounterEndpoints.UpdateEncounter)
            .RequirePermission(Perm.Clinical.EncountersManage)
            .WithValidation<UpdateEncounterRequest>()
            .Audited(AuditEvents.Clinical.EncounterChanged)
            .WithName("UpdateEncounter")
            .WithSummary("Change the chief complaint and diagnosis summary of an open encounter");
        encounters.MapPost("/{id:guid}/close", EncounterEndpoints.CloseEncounter)
            .RequirePermission(Perm.Clinical.EncountersManage)
            .Audited(AuditEvents.Clinical.EncounterClosed)
            .WithName("CloseEncounter")
            .WithSummary("End an open encounter; a clinic visit's appointment becomes completed");
    }
}

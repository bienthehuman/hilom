using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Features.Vitals;
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
        services.AddOptions<VitalRangeOptions>()
            .Bind(configuration.GetSection(VitalRangeOptions.SectionName))
            .Validate(
                options => options.All().All(r => r.Range.Problem() is null),
                "Clinical:VitalRanges has a range whose limits are out of order.")
            .ValidateOnStart();
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

        var vitals = encounters.MapGroup("/{encounterId:guid}/vital-signs");
        vitals.MapGet("", VitalEndpoints.ListVitalSigns)
            .RequirePermission(Perm.Clinical.EncountersView)
            .WithName("ListVitalSigns")
            .WithSummary("The encounter's vital signs, newest first, each value flagged against the ranges. Logged as a view of the encounter.");
        vitals.MapPost("", VitalEndpoints.RecordVitalSigns)
            .RequirePermission(Perm.Clinical.VitalsRecord)
            .WithValidation<RecordVitalSignsRequest>()
            .Audited(AuditEvents.Clinical.VitalsRecorded)
            .WithName("RecordVitalSigns")
            .WithSummary("Record a set of vital signs on an open encounter; at least one measurement");

        endpoints.MapGet("/vital-ranges", VitalEndpoints.GetRanges)
            .RequireSignedIn()
            .WithName("GetVitalRanges")
            .WithSummary("Normal and critical ranges the API flags vital signs against");
    }
}

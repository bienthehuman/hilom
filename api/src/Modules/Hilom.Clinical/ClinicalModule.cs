using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Admissions;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Features.Notes;
using Hilom.Clinical.Features.Vitals;
using Hilom.Clinical.Features.Voiding;
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
        services.AddScoped<IPatientReachSource, WardPatientsThroughBedAssignments>();
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

        var admissions = endpoints.MapGroup("/admissions");
        admissions.MapPost("", AdmissionEndpoints.Admit)
            .RequirePermission(Perm.Clinical.AdmissionsAdmit)
            .WithValidation<AdmitRequest>()
            .Audited(AuditEvents.Clinical.EncounterOpened, AuditEvents.Clinical.PatientAdmitted)
            .WithName("AdmitPatient")
            .WithSummary("Admit a patient into a free bed, from a clinic visit or directly; opens an inpatient encounter");
        admissions.MapGet("/free-beds", AdmissionEndpoints.ListFreeBeds)
            .RequirePermission(Perm.Clinical.AdmissionsAdmit)
            .WithName("ListFreeBeds")
            .WithSummary("Active beds in active wards with nobody in them, grouped by ward");
        admissions.MapGet("/physicians", AdmissionEndpoints.ListAttendingPhysicians)
            .RequirePermission(Perm.Clinical.AdmissionsAdmit)
            .WithName("ListAttendingPhysicians")
            .WithSummary("Current physicians on staff, to choose a stay's attending physician");

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
        vitals.MapPost("/{id:guid}/void", VitalEndpoints.VoidVitalSigns)
            .RequirePermission(Perm.Clinical.RecordsVoid)
            .WithValidation<VoidRecordRequest>()
            .Audited(AuditEvents.Clinical.RecordVoided)
            .WithName("VoidVitalSigns")
            .WithSummary("Void a set of vital signs entered in error, with a reason; it stays in the list, struck through");

        var notes = encounters.MapGroup("/{encounterId:guid}/notes");
        notes.MapGet("", NoteEndpoints.ListNotes)
            .RequirePermission(Perm.Clinical.EncountersView)
            .WithName("ListClinicalNotes")
            .WithSummary("The encounter's notes, newest first, voided ones included. Logged as a view of the encounter.");
        notes.MapPost("", NoteEndpoints.WriteNote)
            .RequirePermission(Perm.Clinical.NotesWrite)
            .WithValidation<WriteNoteRequest>()
            .Audited(AuditEvents.Clinical.NoteWritten)
            .WithName("WriteClinicalNote")
            .WithSummary("Write a note on an open encounter: assessment, progress, nursing or diet");
        notes.MapPut("/{id:guid}", NoteEndpoints.EditNote)
            .RequirePermission(Perm.Clinical.NotesWrite)
            .WithValidation<EditNoteRequest>()
            .Audited(AuditEvents.Clinical.NoteChanged)
            .WithName("EditClinicalNote")
            .WithSummary("The author edits their note within 24 hours of writing it");
        notes.MapPost("/{id:guid}/correct", NoteEndpoints.CorrectNote)
            .RequirePermission(Perm.Clinical.NotesWrite)
            .WithValidation<CorrectNoteRequest>()
            .Audited(AuditEvents.Clinical.NoteWritten, AuditEvents.Clinical.RecordVoided)
            .WithName("CorrectClinicalNote")
            .WithSummary("Write a correction that replaces a note; the original is voided with the reason. Needs clinical.records.void on the original too.");
        notes.MapPost("/{id:guid}/void", NoteEndpoints.VoidNote)
            .RequirePermission(Perm.Clinical.RecordsVoid)
            .WithValidation<VoidRecordRequest>()
            .Audited(AuditEvents.Clinical.RecordVoided)
            .WithName("VoidClinicalNote")
            .WithSummary("Void a note entered in error, with a reason; it stays in the list, struck through");

        endpoints.MapGet("/vital-ranges", VitalEndpoints.GetRanges)
            .RequireSignedIn()
            .WithName("GetVitalRanges")
            .WithSummary("Normal and critical ranges the API flags vital signs against");
    }
}

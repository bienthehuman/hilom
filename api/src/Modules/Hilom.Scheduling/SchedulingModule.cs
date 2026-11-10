using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Features.Registry;
using Hilom.Scheduling.Features.Booking;
using Hilom.Scheduling.Features.Slots;
using Hilom.Scheduling.Scopes;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Scheduling;

/// <summary>Availability slots and appointments.</summary>
public sealed class SchedulingModule : IModule
{
    public const string SchemaName = "scheduling";

    public string Name => "Scheduling";

    public string Schema => SchemaName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPatientReachSource, OwnPatientsThroughAppointments>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/physicians", SlotEndpoints.ListPhysicians)
            .RequirePermission(Perm.Scheduling.SlotsManage)
            .WithName("ListSlotPhysicians")
            .WithSummary("Physicians whose availability the caller may manage: everyone, or only themselves");

        var slots = endpoints.MapGroup("/slots");
        slots.MapGet("", SlotEndpoints.ListSlots)
            .RequirePermission(Perm.Scheduling.SlotsManage)
            .WithName("ListSlots")
            .WithSummary("A physician's slots between two hospital dates (inclusive, up to 62 days)");
        slots.MapPost("/publish", SlotEndpoints.PublishSlots)
            .RequirePermission(Perm.Scheduling.SlotsManage)
            .WithValidation<PublishSlotsRequest>()
            .Audited(AuditEvents.Scheduling.SlotsPublished)
            .WithName("PublishSlots")
            .WithSummary("Publish slots from a weekly pattern; slots overlapping open ones are skipped");
        slots.MapPost("/{id:guid}/close", SlotEndpoints.CloseSlot)
            .RequirePermission(Perm.Scheduling.SlotsManage)
            .Audited(AuditEvents.Scheduling.SlotClosed)
            .WithName("CloseSlot");

        var booking = endpoints.MapGroup("/booking");
        booking.MapGet("/physicians", BookingEndpoints.ListBookablePhysicians)
            .RequirePermission(Perm.Scheduling.SlotsView)
            .WithName("ListBookablePhysicians")
            .WithSummary("Physicians who take appointments, optionally of one specialization");
        booking.MapGet("/slots", BookingEndpoints.ListOpenSlots)
            .RequirePermission(Perm.Scheduling.SlotsView)
            .WithName("ListOpenSlots")
            .WithSummary("Free slots (open, upcoming, not booked) for up to 14 days; no patient data");

        var appointments = endpoints.MapGroup("/appointments");
        appointments.MapPost("", BookingEndpoints.BookAppointment)
            .RequirePermission(Perm.Scheduling.AppointmentsBook)
            .WithValidation<BookAppointmentRequest>()
            .Audited(AuditEvents.Scheduling.AppointmentBooked)
            .WithName("BookAppointment")
            .WithSummary("Book a free slot for a patient; it waits for the physician to confirm");
        appointments.MapGet("/mine", BookingEndpoints.ListMyAppointments)
            .RequirePermission(Perm.Scheduling.AppointmentsView)
            .WithName("ListMyAppointments")
            .WithSummary("Upcoming appointments of the patients linked to the portal user");
    }
}

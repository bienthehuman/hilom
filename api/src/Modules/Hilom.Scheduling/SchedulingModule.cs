using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.Scheduling.Features.Slots;
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
    }
}

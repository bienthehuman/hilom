using Hilom.Facility.Features.Wards;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Facility;

/// <summary>Wards and beds.</summary>
public sealed class FacilityModule : IModule
{
    // Renames are field edits, recorded as data changes; creating and (de)activating are named events.
    private const string EditNotAudited = "Renames and other field edits are recorded as data changes";

    public string Name => "Facility";

    public string Schema => "facility";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var wards = endpoints.MapGroup("/wards");
        wards.MapGet("", WardEndpoints.ListWards)
            .RequirePermission(Perm.Facility.WardsView)
            .WithName("ListWards")
            .WithSummary("Wards with capacity (active beds), sorted by code");
        wards.MapGet("/{id:guid}", WardEndpoints.GetWard)
            .RequirePermission(Perm.Facility.WardsView)
            .WithName("GetWard")
            .WithSummary("A ward and all its beds");
        wards.MapPost("", WardEndpoints.CreateWard)
            .RequirePermission(Perm.Facility.WardsManage)
            .WithValidation<CreateWardRequest>()
            .Audited(AuditEvents.Facility.WardChanged)
            .WithName("CreateWard");
        wards.MapPut("/{id:guid}", WardEndpoints.UpdateWard)
            .RequirePermission(Perm.Facility.WardsManage)
            .WithValidation<UpdateWardRequest>()
            .NotAudited(EditNotAudited)
            .WithName("UpdateWard");
        wards.MapPost("/{id:guid}/deactivate", (Guid id, AppDbContext db, IAuditLog audit, CancellationToken ct) =>
                WardEndpoints.SetWardActive(id, false, db, audit, ct))
            .RequirePermission(Perm.Facility.WardsManage)
            .Audited(AuditEvents.Facility.WardChanged)
            .WithName("DeactivateWard");
        wards.MapPost("/{id:guid}/activate", (Guid id, AppDbContext db, IAuditLog audit, CancellationToken ct) =>
                WardEndpoints.SetWardActive(id, true, db, audit, ct))
            .RequirePermission(Perm.Facility.WardsManage)
            .Audited(AuditEvents.Facility.WardChanged)
            .WithName("ActivateWard");
        wards.MapPost("/{wardId:guid}/beds", WardEndpoints.AddBed)
            .RequirePermission(Perm.Facility.WardsManage)
            .WithValidation<CreateBedRequest>()
            .Audited(AuditEvents.Facility.BedChanged)
            .WithName("AddBed");

        var beds = endpoints.MapGroup("/beds");
        beds.MapPut("/{id:guid}", WardEndpoints.UpdateBed)
            .RequirePermission(Perm.Facility.WardsManage)
            .WithValidation<UpdateBedRequest>()
            .NotAudited(EditNotAudited)
            .WithName("UpdateBed");
        beds.MapPost("/{id:guid}/deactivate", (Guid id, AppDbContext db, IAuditLog audit, CancellationToken ct) =>
                WardEndpoints.SetBedActive(id, false, db, audit, ct))
            .RequirePermission(Perm.Facility.WardsManage)
            .Audited(AuditEvents.Facility.BedChanged)
            .WithName("DeactivateBed")
            .WithSummary("Take a bed out of service");
        beds.MapPost("/{id:guid}/activate", (Guid id, AppDbContext db, IAuditLog audit, CancellationToken ct) =>
                WardEndpoints.SetBedActive(id, true, db, audit, ct))
            .RequirePermission(Perm.Facility.WardsManage)
            .Audited(AuditEvents.Facility.BedChanged)
            .WithName("ActivateBed");
    }
}

using Hilom.Access;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Facility;
using Hilom.Workforce.Features.Assignments;
using Hilom.Workforce.Features.Employees;
using Hilom.Workforce.Features.Invites;
using Hilom.Workforce.Features.Lookups;
using Hilom.Workforce.Features.Roster;
using Hilom.Workforce.Features.Shifts;
using Hilom.Workforce.Features.Swaps;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Workforce;

/// <summary>Employees, physicians, invites, shifts, ward assignments, roster and swaps.</summary>
public sealed class WorkforceModule : IModule
{
    public string Name => "Workforce";

    public const string SchemaName = "workforce";

    // Shift definitions are reference data with no catalog event; every change is still a data change entry.
    private const string ShiftsNotAudited = "Reference data; edits are recorded as data changes";

    public string Schema => SchemaName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InviteOptions>()
            .Bind(configuration.GetSection(InviteOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IDepartmentDirectory, DepartmentDirectory>();
        services.AddScoped<IStaffScope, StaffScope>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapLookup<Department>("departments");
        endpoints.MapLookup<Position>("positions");
        endpoints.MapLookup<Specialization>("specializations");

        var employees = endpoints.MapGroup("/employees");
        employees.MapGet("", EmployeeEndpoints.ListEmployees)
            .RequirePermission(Perm.Workforce.EmployeesView)
            .WithName("ListEmployees")
            .WithSummary("Employees in the caller's scope, paged, with search and filters");
        employees.MapGet("/{id:guid}", EmployeeEndpoints.GetEmployee)
            .RequirePermission(Perm.Workforce.EmployeesView)
            .WithName("GetEmployee")
            .WithSummary("One employee's profile; 404 when outside the caller's scope");
        employees.MapPost("", EmployeeEndpoints.CreateEmployee)
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .WithValidation<CreateEmployeeRequest>()
            .Audited(AuditEvents.Workforce.EmployeeCreated)
            .WithName("CreateEmployee");
        employees.MapPut("/{id:guid}", EmployeeEndpoints.UpdateEmployee)
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .WithValidation<UpdateEmployeeRequest>()
            .NotAudited("Field edits are recorded as data changes; status has its own audited action")
            .WithName("UpdateEmployee");
        employees.MapPost("/{id:guid}/status", EmployeeEndpoints.ChangeEmployeeStatus)
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .WithValidation<ChangeEmployeeStatusRequest>()
            .Audited(AuditEvents.Workforce.EmployeeStatusChanged)
            .WithName("ChangeEmployeeStatus")
            .WithSummary("Mark active, on leave, or separated");

        employees.MapGet("/{employeeId:guid}/invites", InviteEndpoints.ListInvites)
            .RequirePermission(Perm.Workforce.InvitesSend)
            .WithName("ListInvites")
            .WithSummary("Invites sent to an employee, newest first");
        employees.MapPost("/{employeeId:guid}/invites", InviteEndpoints.SendInvite)
            .RequirePermission(Perm.Workforce.InvitesSend)
            .WithValidation<SendInviteRequest>()
            .Audited(AuditEvents.Workforce.InviteSent)
            .WithName("SendInvite")
            .WithSummary("Email an invite to set a password; the account gets the chosen roles");

        var shifts = endpoints.MapGroup("/shifts");
        shifts.MapGet("", ShiftEndpoints.ListShifts)
            .RequireSignedIn()
            .WithName("ListShifts")
            .WithSummary("Shift definitions in roster order. Any signed-in user can read them (rosters, schedules).");
        shifts.MapPost("", ShiftEndpoints.CreateShift)
            .RequirePermission(Perm.Workforce.ShiftsManage)
            .WithValidation<CreateShiftRequest>()
            .NotAudited(ShiftsNotAudited)
            .WithName("CreateShift");
        shifts.MapPut("/{id:guid}", ShiftEndpoints.UpdateShift)
            .RequirePermission(Perm.Workforce.ShiftsManage)
            .WithValidation<UpdateShiftRequest>()
            .NotAudited(ShiftsNotAudited)
            .WithName("UpdateShift");
        shifts.MapPost("/{id:guid}/deactivate", (Guid id, AppDbContext db, CancellationToken ct) => ShiftEndpoints.SetShiftActive(id, false, db, ct))
            .RequirePermission(Perm.Workforce.ShiftsManage)
            .NotAudited(ShiftsNotAudited)
            .WithName("DeactivateShift")
            .WithSummary("Hide from the roster for new entries; existing entries keep it");
        shifts.MapPost("/{id:guid}/activate", (Guid id, AppDbContext db, CancellationToken ct) => ShiftEndpoints.SetShiftActive(id, true, db, ct))
            .RequirePermission(Perm.Workforce.ShiftsManage)
            .NotAudited(ShiftsNotAudited)
            .WithName("ActivateShift");

        var assignments = endpoints.MapGroup("/ward-assignments");
        assignments.MapGet("", AssignmentEndpoints.ListAssignments)
            .RequirePermission(Perm.Workforce.AssignmentsView)
            .WithName("ListWardAssignments")
            .WithSummary("Ward assignments in the caller's scope (all, my wards today, or my own), newest first");
        assignments.MapPost("", AssignmentEndpoints.AssignWard)
            .RequirePermission(Perm.Workforce.AssignmentsManage)
            .WithValidation<AssignWardRequest>()
            .Audited(AuditEvents.Workforce.WardAssignmentChanged)
            .WithName("AssignWard")
            .WithSummary("Assign an employee to a ward from a date. One ward at a time: overlaps return 409.");
        assignments.MapPost("/{id:guid}/end", AssignmentEndpoints.EndAssignment)
            .RequirePermission(Perm.Workforce.AssignmentsManage)
            .WithValidation<EndAssignmentRequest>()
            .Audited(AuditEvents.Workforce.WardAssignmentChanged)
            .WithName("EndWardAssignment")
            .WithSummary("Set the last day on the ward");

        var roster = endpoints.MapGroup("/roster");
        roster.MapGet("/wards", RosterEndpoints.ListRosterWards)
            .RequirePermission(Perm.Workforce.RosterView)
            .WithName("ListRosterWards")
            .WithSummary("Wards whose roster the caller can open (all active wards, or their wards today)");
        roster.MapGet("/wards/{wardId:guid}", RosterEndpoints.GetWardRoster)
            .RequirePermission(Perm.Workforce.RosterView)
            .WithName("GetWardRoster")
            .WithSummary("A ward's staff and entries for up to 31 days (default: this week, Monday to Sunday)");
        roster.MapGet("/staffing", RosterEndpoints.GetStaffing)
            .RequirePermission(Perm.Workforce.RosterView)
            .WithName("GetStaffing")
            .WithSummary("On-duty counts per ward and shift for one day (default: today), for the wards whose roster the caller can open");
        roster.MapGet("/mine", RosterEndpoints.GetMySchedule)
            .RequirePermission(Perm.Workforce.RosterView)
            .WithName("GetMySchedule")
            .WithSummary("The signed-in user's own duty days");
        roster.MapPost("/entries", RosterEndpoints.AddEntry)
            .RequirePermission(Perm.Workforce.RosterManage)
            .WithValidation<AddRosterEntryRequest>()
            .Audited(AuditEvents.Workforce.RosterChanged)
            .WithName("AddRosterEntry")
            .WithSummary("Put someone on a shift. One shift per day, on a ward they're assigned to that day.");
        roster.MapPut("/entries/{id:guid}", RosterEndpoints.ChangeShift)
            .RequirePermission(Perm.Workforce.RosterManage)
            .WithValidation<ChangeRosterShiftRequest>()
            .Audited(AuditEvents.Workforce.RosterChanged)
            .WithName("ChangeRosterShift");
        roster.MapDelete("/entries/{id:guid}", RosterEndpoints.RemoveEntry)
            .RequirePermission(Perm.Workforce.RosterManage)
            .Audited(AuditEvents.Workforce.RosterChanged)
            .WithName("RemoveRosterEntry");

        var swaps = endpoints.MapGroup("/swaps");
        swaps.MapGet("/mine", SwapEndpoints.ListMySwaps)
            .RequirePermission(Perm.Workforce.SwapsRequest)
            .WithName("ListMySwaps")
            .WithSummary("Swaps you asked for or that are aimed at you, newest first");
        swaps.MapGet("/review", SwapEndpoints.ListSwapsForReview)
            .RequirePermission(Perm.Workforce.SwapsApprove)
            .WithName("ListSwapsForReview")
            .WithSummary("Swaps on wards you approve for (default: waiting for approval), soonest shift first");
        swaps.MapGet("/options", SwapEndpoints.ListSwapOptions)
            .RequirePermission(Perm.Workforce.SwapsRequest)
            .WithName("ListSwapOptions")
            .WithSummary("Colleagues' upcoming shifts on the same ward that could be swapped for one of yours");
        swaps.MapPost("", SwapEndpoints.RequestSwap)
            .RequirePermission(Perm.Workforce.SwapsRequest)
            .WithValidation<RequestSwapRequest>()
            .Audited(AuditEvents.Workforce.SwapRequested)
            .WithName("RequestSwap");
        swaps.MapPost("/{id:guid}/accept", SwapEndpoints.Accept)
            .RequirePermission(Perm.Workforce.SwapsRequest)
            .WithValidation<SwapActionRequest>()
            .Audited(AuditEvents.Workforce.SwapAccepted)
            .WithName("AcceptSwap");
        swaps.MapPost("/{id:guid}/decline", SwapEndpoints.Decline)
            .RequirePermission(Perm.Workforce.SwapsRequest)
            .WithValidation<SwapActionRequest>()
            .Audited(AuditEvents.Workforce.SwapDeclined)
            .WithName("DeclineSwap");
        swaps.MapPost("/{id:guid}/cancel", SwapEndpoints.Cancel)
            .RequirePermission(Perm.Workforce.SwapsRequest)
            .WithValidation<SwapActionRequest>()
            .Audited(AuditEvents.Workforce.SwapCancelled)
            .WithName("CancelSwap");
        swaps.MapPost("/{id:guid}/approve", SwapEndpoints.Approve)
            .RequirePermission(Perm.Workforce.SwapsApprove)
            .WithValidation<SwapActionRequest>()
            .Audited(AuditEvents.Workforce.SwapApproved)
            .WithName("ApproveSwap")
            .WithSummary("Exchange the two shifts, after re-checking assignments and one shift per day");
        swaps.MapPost("/{id:guid}/reject", SwapEndpoints.Reject)
            .RequirePermission(Perm.Workforce.SwapsApprove)
            .WithValidation<SwapActionRequest>()
            .Audited(AuditEvents.Workforce.SwapRejected)
            .WithName("RejectSwap");

        var invites = endpoints.MapGroup("/invites");
        invites.MapPost("/{id:guid}/revoke", InviteEndpoints.RevokeInvite)
            .RequirePermission(Perm.Workforce.InvitesSend)
            .Audited(AuditEvents.Workforce.InviteRevoked)
            .WithName("RevokeInvite");

        // The token travels in the body, not the URL, so it stays out of logs.
        invites.MapPost("/lookup", InviteEndpoints.LookupInvite)
            .AllowAnonymous()
            .RequireRateLimiting(AccessModule.LoginRateLimitPolicy)
            .WithValidation<InviteTokenRequest>()
            .NotAudited("Only reads the invite to greet the person")
            .WithName("LookupInvite")
            .WithSummary("Who an invite link is for, or why it no longer works");
        invites.MapPost("/accept", InviteEndpoints.AcceptInvite)
            .AllowAnonymous()
            .RequireRateLimiting(AccessModule.LoginRateLimitPolicy)
            .WithValidation<AcceptInviteRequest>()
            .Audited(AuditEvents.Auth.InviteAccepted)
            .WithName("AcceptInvite")
            .WithSummary("Set a password, create the account and link it to the employee");
    }
}

using Hilom.Access;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Facility;
using Hilom.Workforce.Features.Employees;
using Hilom.Workforce.Features.Invites;
using Hilom.Workforce.Features.Lookups;
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

    public string Schema => SchemaName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InviteOptions>()
            .Bind(configuration.GetSection(InviteOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IDepartmentDirectory, DepartmentDirectory>();
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

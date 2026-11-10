using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Features.Employees;
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
    }
}

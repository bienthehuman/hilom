using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Features.Employees;

/// <summary>Which employee records a scope reaches: <c>all</c>, or <c>own</c> = the user's own record (docs/RBAC.md).</summary>
internal static class EmployeeScope
{
    public static IQueryable<Employee> WhereInScope(this IQueryable<Employee> employees, AccessScope scope) =>
        scope switch
        {
            { IsAll: true } => employees,
            { UserId: { } userId } when scope.Includes(PermissionScope.Own) => employees.Where(e => e.UserId == userId),
            _ => employees.Where(_ => false),
        };

    public static bool Reaches(this AccessScope scope, Employee employee) =>
        scope.IsAll || (scope.Includes(PermissionScope.Own) && scope.UserId is { } userId && employee.UserId == userId);
}

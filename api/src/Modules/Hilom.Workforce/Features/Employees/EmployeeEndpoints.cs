using System.Globalization;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Workforce.Features.Employees;

/// <summary>Employee records: list, profile, create, edit, change status (docs/DATA_MODEL.md, workforce).</summary>
internal static class EmployeeEndpoints
{
    private static readonly string[] SortFields = ["name", "employeeNo", "hiredOn"];

    public static async Task<Ok<PagedResponse<EmployeeSummaryResponse>>> ListEmployees(
        AppDbContext db,
        ICurrentPermissions permissions,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        Guid? departmentId,
        ProfessionalType? professionalType,
        EmployeeStatus? status,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var (field, descending) = Paging.ParseSort(sort, SortFields, "name");
        var scope = await permissions.ScopeForAsync(Perm.Workforce.EmployeesView, cancellationToken);

        var query = db.Set<Employee>().AsNoTracking().WhereInScope(scope);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.FirstName + " " + e.LastName, pattern)
                || EF.Functions.ILike(e.LastName + ", " + e.FirstName, pattern)
                || EF.Functions.ILike(e.EmployeeNo, pattern)
                || (e.LicenseNumber != null && EF.Functions.ILike(e.LicenseNumber, pattern)));
        }

        if (departmentId is { } department)
        {
            query = query.Where(e => e.DepartmentId == department);
        }

        if (professionalType is { } type)
        {
            query = query.Where(e => e.ProfessionalType == type);
        }

        if (status is { } employeeStatus)
        {
            query = query.Where(e => e.Status == employeeStatus);
        }

        query = (field, descending) switch
        {
            ("employeeNo", false) => query.OrderBy(e => e.EmployeeNo),
            ("employeeNo", true) => query.OrderByDescending(e => e.EmployeeNo),
            ("hiredOn", false) => query.OrderBy(e => e.HiredOn).ThenBy(e => e.EmployeeNo),
            ("hiredOn", true) => query.OrderByDescending(e => e.HiredOn).ThenByDescending(e => e.EmployeeNo),
            (_, true) => query.OrderByDescending(e => e.LastName).ThenByDescending(e => e.FirstName).ThenByDescending(e => e.EmployeeNo),
            _ => query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.EmployeeNo),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await (
            from e in query.Skip((pageNumber - 1) * size).Take(size)
            join d in db.Set<Department>() on e.DepartmentId equals d.Id
            join p in db.Set<Position>() on e.PositionId equals p.Id
            select new EmployeeSummaryResponse(
                e.Id,
                e.EmployeeNo,
                e.FirstName,
                e.MiddleName,
                e.LastName,
                new LookupRef(d.Id, d.Name, d.IsActive),
                new LookupRef(p.Id, p.Name, p.IsActive),
                e.ProfessionalType,
                e.Status,
                e.UserId != null))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<EmployeeSummaryResponse>(items, total, pageNumber, size));
    }

    public static async Task<Results<Ok<EmployeeResponse>, ProblemHttpResult>> GetEmployee(
        Guid id,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        var scope = await permissions.ScopeForAsync(Perm.Workforce.EmployeesView, cancellationToken);
        if (!scope.Reaches(employee))
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Workforce.EmployeesView, Target(employee), cancellationToken);
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        return TypedResults.Ok(await ToResponseAsync(db, employee, cancellationToken));
    }

    public static async Task<Results<Created<EmployeeResponse>, ValidationProblem>> CreateEmployee(
        CreateEmployeeRequest request,
        AppDbContext db,
        HospitalClock clock,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var details = request.ToDetails();
        if (await CheckReferencesAsync(db, details, current: null, cancellationToken) is { } invalid)
        {
            return invalid.ToValidationResult();
        }

        var created = Employee.Create(await NextEmployeeNoAsync(db, clock, cancellationToken), details);
        if (!created.IsSuccess)
        {
            return created.Error.ToValidationResult();
        }

        var employee = created.Value;
        db.Add(employee);
        auditLog.Record(
            AuditEvents.Workforce.EmployeeCreated,
            new AuditOptions
            {
                Target = Target(employee),
                Details = new
                {
                    employeeNo = employee.EmployeeNo,
                    name = $"{employee.LastName}, {employee.FirstName}",
                    professionalType = EnumText.ToText(employee.ProfessionalType),
                },
            });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/workforce/employees/{employee.Id}",
            await ToResponseAsync(db, employee, cancellationToken));
    }

    public static async Task<Results<Ok<EmployeeResponse>, ProblemHttpResult, ValidationProblem>> UpdateEmployee(
        Guid id,
        UpdateEmployeeRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        var details = request.ToDetails();
        if (await CheckReferencesAsync(db, details, employee, cancellationToken) is { } invalid)
        {
            return invalid.ToValidationResult();
        }

        var updated = employee.Update(details);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToValidationResult();
        }

        await SaveWithVersionAsync(db, employee, request.Version, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, employee, cancellationToken));
    }

    public static async Task<Results<Ok<EmployeeResponse>, ProblemHttpResult, ValidationProblem>> ChangeEmployeeStatus(
        Guid id,
        ChangeEmployeeStatusRequest request,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        var before = new { employee.Status, employee.SeparatedOn };
        var changed = employee.ChangeStatus(request.Status!.Value, request.SeparatedOn);
        if (!changed.IsSuccess)
        {
            return changed.Error.ToValidationResult();
        }

        if (before.Status == employee.Status && before.SeparatedOn == employee.SeparatedOn)
        {
            auditLog.NothingToRecord();
        }
        else
        {
            auditLog.Record(
                AuditEvents.Workforce.EmployeeStatusChanged,
                new AuditOptions
                {
                    Target = Target(employee),
                    Details = new
                    {
                        employeeNo = employee.EmployeeNo,
                        from = EnumText.ToText(before.Status),
                        to = EnumText.ToText(employee.Status),
                        separatedOn = employee.SeparatedOn,
                    },
                });
        }

        await SaveWithVersionAsync(db, employee, request.Version, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, employee, cancellationToken));
    }

    /// <summary>Always writes the row, so a stale version is caught even when nothing else changed.</summary>
    private static Task<int> SaveWithVersionAsync(AppDbContext db, Employee employee, uint version, CancellationToken cancellationToken)
    {
        var entry = db.Entry(employee);
        entry.Property(e => e.Version).OriginalValue = version;
        entry.Property(e => e.UpdatedAt).IsModified = true;
        return db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// New or changed department, position and specialization must exist and be active. Keeping an entry that
    /// was deactivated after it was chosen is fine, so editing other fields still works.
    /// </summary>
    private static async Task<Error?> CheckReferencesAsync(
        AppDbContext db,
        EmployeeDetails details,
        Employee? current,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (details.DepartmentId != current?.DepartmentId
            && !await IsActiveAsync<Department>(db, details.DepartmentId, cancellationToken))
        {
            errors["departmentId"] = ["Choose a department from the list."];
        }

        if (details.PositionId != current?.PositionId
            && !await IsActiveAsync<Position>(db, details.PositionId, cancellationToken))
        {
            errors["positionId"] = ["Choose a position from the list."];
        }

        if (details.Physician is { } physician
            && physician.SpecializationId != current?.Physician?.SpecializationId
            && !await IsActiveAsync<Specialization>(db, physician.SpecializationId, cancellationToken))
        {
            errors["physician.specializationId"] = ["Choose a specialization from the list."];
        }

        return errors.Count == 0 ? null : WorkforceErrors.InvalidEmployee(errors);
    }

    private static Task<bool> IsActiveAsync<T>(AppDbContext db, Guid id, CancellationToken cancellationToken)
        where T : Lookup =>
        db.Set<T>().AnyAsync(l => l.Id == id && l.IsActive, cancellationToken);

    /// <summary><c>EMP-2026-0042</c>: the year it was created (hospital time) and a never-reset counter.</summary>
    private static async Task<string> NextEmployeeNoAsync(AppDbContext db, HospitalClock clock, CancellationToken cancellationToken)
    {
        var next = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{WorkforceModule.SchemaName}.{EmployeeConfiguration.NumberSequence}') AS \"Value\"")
            .SingleAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"EMP-{clock.Today.Year}-{next:D4}");
    }

    private static async Task<EmployeeResponse> ToResponseAsync(AppDbContext db, Employee e, CancellationToken cancellationToken)
    {
        var department = await LookupRefAsync<Department>(db, e.DepartmentId, cancellationToken);
        var position = await LookupRefAsync<Position>(db, e.PositionId, cancellationToken);
        var specialization = e.Physician is { } profile
            ? await LookupRefAsync<Specialization>(db, profile.SpecializationId, cancellationToken)
            : null;

        return new EmployeeResponse(
            e.Id,
            e.EmployeeNo,
            e.FirstName,
            e.MiddleName,
            e.LastName,
            e.Sex,
            e.BirthDate,
            e.ContactNumber,
            e.Email,
            department,
            position,
            e.ProfessionalType,
            e.LicenseNumber,
            e.EmploymentType,
            e.HiredOn,
            e.Status,
            e.SeparatedOn,
            e.Physician is { } physician && specialization is not null
                ? new PhysicianProfileResponse(specialization, physician.AcceptsAppointments, physician.ConsultationMinutes)
                : null,
            e.UserId is not null,
            e.Version);
    }

    private static Task<LookupRef> LookupRefAsync<T>(AppDbContext db, Guid id, CancellationToken cancellationToken)
        where T : Lookup =>
        db.Set<T>().Where(l => l.Id == id).Select(l => new LookupRef(l.Id, l.Name, l.IsActive)).SingleAsync(cancellationToken);

    private static AuditTarget Target(Employee employee) => new(nameof(Employee), employee.Id);
}

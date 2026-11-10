using Hilom.Access.Domain;
using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Persistence;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Workforce.Features.Assignments;

/// <summary>
/// Ward assignments: who works on which ward, over which dates. Every change is a critical audit event,
/// because it changes which patients the person can see (docs/AUDIT.md).
/// </summary>
internal static class AssignmentEndpoints
{
    public static async Task<Ok<PagedResponse<AssignmentResponse>>> ListAssignments(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        HospitalClock clock,
        int? page,
        int? pageSize,
        Guid? wardId,
        Guid? employeeId,
        DateOnly? on,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var reach = await AssignmentReach.ResolveAsync(
            await permissions.ScopeForAsync(Perm.Workforce.AssignmentsView, cancellationToken),
            staff,
            cancellationToken);

        var query = reach.Filter(db.Set<WardAssignment>().AsNoTracking());
        if (wardId is { } ward)
        {
            query = query.Where(a => a.WardId == ward);
        }

        if (employeeId is { } employee)
        {
            query = query.Where(a => a.EmployeeId == employee);
        }

        if (on is { } day)
        {
            query = query.Where(a => a.Period.Contains(day));
        }

        var total = await query.CountAsync(cancellationToken);
        var assignments = await query
            .OrderByDescending(a => a.Period.LowerBound)
            .ThenBy(a => a.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var items = await ToResponsesAsync(db, assignments, clock.Today, cancellationToken);
        return TypedResults.Ok(new PagedResponse<AssignmentResponse>(items, total, pageNumber, size));
    }

    public static async Task<Results<Created<AssignmentResponse>, ProblemHttpResult, ValidationProblem>> AssignWard(
        AssignWardRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        var reach = await AssignmentReach.ResolveAsync(
            await permissions.ScopeForAsync(Perm.Workforce.AssignmentsManage, cancellationToken),
            staff,
            cancellationToken);

        var ward = await db.Set<Ward>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == request.WardId, cancellationToken);
        if (ward is null)
        {
            return WorkforceErrors.WardUnavailable.ToValidationResult();
        }

        if (!reach.ReachesWard(ward.Id))
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Workforce.AssignmentsManage, new AuditTarget(nameof(Ward), ward.Id), cancellationToken);
            return FacilityErrors.WardNotFound.ToProblemResult();
        }

        if (!ward.IsActive)
        {
            return WorkforceErrors.WardUnavailable.ToValidationResult();
        }

        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return WorkforceErrors.EmployeeNotFound.ToProblemResult();
        }

        if (employee.Status == EmployeeStatus.Separated)
        {
            return WorkforceErrors.EmployeeSeparated.ToProblemResult();
        }

        var created = WardAssignment.Create(
            employee.Id,
            ward.Id,
            request.StartsOn ?? clock.Today,
            request.LastDay,
            request.Reason,
            currentUser.UserId);
        if (!created.IsSuccess)
        {
            return created.Error.ToValidationResult();
        }

        var assignment = created.Value;
        if (await OverlapAsync(db, assignment, cancellationToken) is { } overlap)
        {
            return overlap.ToProblemResult();
        }

        db.Add(assignment);
        auditLog.Record(
            AuditEvents.Workforce.WardAssignmentChanged,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(WardAssignment), assignment.Id),
                Details = new
                {
                    change = "assigned",
                    employeeId = employee.Id,
                    employeeNo = employee.EmployeeNo,
                    ward = ward.Code,
                    startsOn = assignment.StartsOn,
                    lastDay = assignment.LastDay,
                    reason = assignment.Reason,
                },
            });

        if (await SaveAsync(db, cancellationToken) is { } raced)
        {
            return raced.ToProblemResult();
        }

        var response = (await ToResponsesAsync(db, [assignment], clock.Today, cancellationToken))[0];
        return TypedResults.Created($"/api/workforce/ward-assignments/{assignment.Id}", response);
    }

    public static async Task<Results<Ok<AssignmentResponse>, ProblemHttpResult, ValidationProblem>> EndAssignment(
        Guid id,
        EndAssignmentRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        var assignment = await db.Set<WardAssignment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (assignment is null)
        {
            return WorkforceErrors.AssignmentNotFound.ToProblemResult();
        }

        var reach = await AssignmentReach.ResolveAsync(
            await permissions.ScopeForAsync(Perm.Workforce.AssignmentsManage, cancellationToken),
            staff,
            cancellationToken);
        if (!reach.ReachesWard(assignment.WardId))
        {
            await auditLog.RecordOutOfScopeAsync(
                Perm.Workforce.AssignmentsManage,
                new AuditTarget(nameof(WardAssignment), assignment.Id),
                cancellationToken);
            return WorkforceErrors.AssignmentNotFound.ToProblemResult();
        }

        var before = assignment.LastDay;
        var ended = assignment.End(request.LastDay!.Value);
        if (!ended.IsSuccess)
        {
            return ended.Error.ToValidationResult();
        }

        var ward = await db.Set<Ward>().Where(w => w.Id == assignment.WardId).Select(w => w.Code).SingleAsync(cancellationToken);
        auditLog.Record(
            AuditEvents.Workforce.WardAssignmentChanged,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(WardAssignment), assignment.Id),
                Details = new
                {
                    change = "ended",
                    employeeId = assignment.EmployeeId,
                    ward,
                    startsOn = assignment.StartsOn,
                    lastDayBefore = before,
                    lastDay = assignment.LastDay,
                },
            });

        db.Entry(assignment).Property(a => a.Version).OriginalValue = request.Version;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [assignment], clock.Today, cancellationToken))[0]);
    }

    /// <summary>A friendly message naming the assignment in the way. The database has the final say (SaveAsync).</summary>
    private static async Task<Error?> OverlapAsync(AppDbContext db, WardAssignment assignment, CancellationToken cancellationToken)
    {
        var period = assignment.Period;
        var conflict = await (
            from a in db.Set<WardAssignment>().AsNoTracking()
            join w in db.Set<Ward>() on a.WardId equals w.Id
            where a.EmployeeId == assignment.EmployeeId && a.Period.Overlaps(period)
            orderby a.Period.LowerBound
            select new { a.Period, w.Code })
            .FirstOrDefaultAsync(cancellationToken);
        if (conflict is null)
        {
            return null;
        }

        var lastDay = conflict.Period.UpperBoundInfinite ? (DateOnly?)null : conflict.Period.UpperBound.AddDays(-1);
        return WorkforceErrors.AssignmentOverlaps(conflict.Code, conflict.Period.LowerBound, lastDay);
    }

    /// <summary>Saves; if someone assigned the same person at the same moment, the exclusion constraint answers.</summary>
    private static async Task<Error?> SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: WardAssignmentConfiguration.OneWardAtATime,
        })
        {
            return WorkforceErrors.AssignmentRaced;
        }
    }

    private static async Task<List<AssignmentResponse>> ToResponsesAsync(
        AppDbContext db,
        IReadOnlyCollection<WardAssignment> assignments,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var employeeIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var wardIds = assignments.Select(a => a.WardId).Distinct().ToList();
        var userIds = assignments.Select(a => a.AssignedBy).OfType<Guid>().Distinct().ToList();

        var employees = await db.Set<Employee>()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new AssignedEmployee(e.Id, e.EmployeeNo, e.FirstName, e.LastName, e.ProfessionalType), cancellationToken);
        var wards = await db.Set<Ward>()
            .Where(w => wardIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => new AssignedWard(w.Id, w.Code, w.Name), cancellationToken);
        var users = await db.Set<User>()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return
        [
            .. assignments.Select(a => new AssignmentResponse(
                a.Id,
                employees[a.EmployeeId],
                wards[a.WardId],
                a.StartsOn,
                a.LastDay,
                a.StartsOn > today ? AssignmentState.Upcoming : a.IsOn(today) ? AssignmentState.Current : AssignmentState.Past,
                a.Reason,
                a.AssignedBy is { } by ? users.GetValueOrDefault(by) : null,
                a.Version)),
        ];
    }
}

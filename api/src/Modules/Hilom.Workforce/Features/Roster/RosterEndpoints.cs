using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Features.Assignments;
using Hilom.Workforce.Persistence;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Workforce.Features.Roster;

/// <summary>
/// The duty roster: who works which shift, on which ward, on which day. One shift per person per day, and
/// only on a ward they're assigned to that day.
/// </summary>
internal static class RosterEndpoints
{
    private const int MaxDays = 31;

    /// <summary>Wards whose whole roster the user can open: every active ward, or the wards they're on today.</summary>
    public static async Task<Ok<List<AssignedWard>>> ListRosterWards(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        CancellationToken cancellationToken)
    {
        var reach = await ReachAsync(permissions, staff, Perm.Workforce.RosterView, cancellationToken);
        var wardIds = reach.WardIds;
        var all = reach.All;
        var wards = await db.Set<Ward>()
            .Where(w => w.IsActive && (all || wardIds.Contains(w.Id)))
            .Select(w => new AssignedWard(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(wards.OrderBy(w => w.Code, NaturalOrder).ToList());
    }

    public static async Task<Results<Ok<WardRosterResponse>, ProblemHttpResult, ValidationProblem>> GetWardRoster(
        Guid wardId,
        DateOnly? from,
        DateOnly? to,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        if (Range(from, to, clock) is not { } range)
        {
            return WorkforceErrors.RosterRangeInvalid.ToValidationResult();
        }

        var (start, end) = range;

        var ward = await db.Set<Ward>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == wardId, cancellationToken);
        if (ward is null)
        {
            return FacilityErrors.WardNotFound.ToProblemResult();
        }

        var reach = await ReachAsync(permissions, staff, Perm.Workforce.RosterView, cancellationToken);
        if (!reach.ReachesWard(wardId))
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Workforce.RosterView, new AuditTarget(nameof(Ward), wardId), cancellationToken);
            return FacilityErrors.WardNotFound.ToProblemResult();
        }

        var period = WardAssignment.PeriodOf(start, end);
        var assignments = await db.Set<WardAssignment>().AsNoTracking()
            .Where(a => a.WardId == wardId && a.Period.Overlaps(period))
            .ToListAsync(cancellationToken);
        var entries = await db.Set<RosterEntry>().AsNoTracking()
            .Where(r => r.WardId == wardId && r.DutyDate >= start && r.DutyDate <= end && r.Status != RosterStatus.SwappedOut)
            .ToListAsync(cancellationToken);

        var employeeIds = assignments.Select(a => a.EmployeeId).Concat(entries.Select(e => e.EmployeeId)).Distinct().ToList();
        var employees = await db.Set<Employee>()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new AssignedEmployee(e.Id, e.EmployeeNo, e.FirstName, e.LastName, e.ProfessionalType))
            .ToListAsync(cancellationToken);

        var staffRows = employees
            .OrderBy(e => e.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new RosterStaff(
                e,
                [.. assignments.Where(a => a.EmployeeId == e.Id).OrderBy(a => a.StartsOn).Select(a => new AssignedPeriod(a.StartsOn, a.LastDay))]))
            .ToList();

        return TypedResults.Ok(new WardRosterResponse(
            new AssignedWard(ward.Id, ward.Code, ward.Name),
            start,
            end,
            staffRows,
            await ToResponsesAsync(db, entries, cancellationToken)));
    }

    /// <summary>The signed-in user's own duty days, on any ward.</summary>
    public static async Task<Results<Ok<MyScheduleResponse>, ValidationProblem>> GetMySchedule(
        DateOnly? from,
        DateOnly? to,
        AppDbContext db,
        IStaffScope staff,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        if (Range(from, to, clock) is not { } range)
        {
            return WorkforceErrors.RosterRangeInvalid.ToValidationResult();
        }

        var (start, end) = range;

        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } me)
        {
            return TypedResults.Ok(new MyScheduleResponse(false, start, end, []));
        }

        var entries = await db.Set<RosterEntry>().AsNoTracking()
            .Where(r => r.EmployeeId == me && r.DutyDate >= start && r.DutyDate <= end && r.Status != RosterStatus.SwappedOut)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new MyScheduleResponse(true, start, end, await ToResponsesAsync(db, entries, cancellationToken)));
    }

    public static async Task<Results<Created<RosterEntryResponse>, ProblemHttpResult, ValidationProblem>> AddEntry(
        AddRosterEntryRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var day = request.DutyDate!.Value;
        var ward = await db.Set<Ward>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == request.WardId, cancellationToken);
        if (ward is null)
        {
            return WorkforceErrors.WardUnavailable.ToValidationResult();
        }

        var reach = await ReachAsync(permissions, staff, Perm.Workforce.RosterManage, cancellationToken);
        if (!reach.ReachesWard(ward.Id))
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Workforce.RosterManage, new AuditTarget(nameof(Ward), ward.Id), cancellationToken);
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

        var shift = await db.Set<Shift>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ShiftId && s.IsActive, cancellationToken);
        if (shift is null)
        {
            return WorkforceErrors.ShiftUnavailable.ToValidationResult();
        }

        var name = $"{employee.FirstName} {employee.LastName}";
        var assigned = await db.Set<WardAssignment>()
            .AnyAsync(a => a.EmployeeId == employee.Id && a.WardId == ward.Id && a.Period.Contains(day), cancellationToken);
        if (!assigned)
        {
            return WorkforceErrors.NotAssignedToWard(name, ward.Code, day).ToProblemResult();
        }

        var clash = await (
            from r in db.Set<RosterEntry>()
            join s in db.Set<Shift>() on r.ShiftId equals s.Id
            join w in db.Set<Ward>() on r.WardId equals w.Id
            where r.EmployeeId == employee.Id && r.DutyDate == day && r.Status == RosterStatus.Scheduled
            select new { Shift = s.Code, Ward = w.Code })
            .FirstOrDefaultAsync(cancellationToken);
        if (clash is not null)
        {
            return WorkforceErrors.AlreadyRostered(name, clash.Shift, clash.Ward, day).ToProblemResult();
        }

        var entry = new RosterEntry(employee.Id, ward.Id, shift.Id, day);
        db.Add(entry);
        auditLog.Record(
            AuditEvents.Workforce.RosterChanged,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(RosterEntry), entry.Id),
                Details = new { change = "added", employeeNo = employee.EmployeeNo, ward = ward.Code, shift = shift.Code, dutyDate = day },
            });

        if (await SaveAsync(db, cancellationToken) is { } raced)
        {
            return raced.ToProblemResult();
        }

        var response = (await ToResponsesAsync(db, [entry], cancellationToken))[0];
        return TypedResults.Created($"/api/workforce/roster/entries/{entry.Id}", response);
    }

    public static async Task<Results<Ok<RosterEntryResponse>, ProblemHttpResult, ValidationProblem>> ChangeShift(
        Guid id,
        ChangeRosterShiftRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var entry = await FindManageableAsync(db, permissions, staff, auditLog, id, cancellationToken);
        if (entry is null)
        {
            return WorkforceErrors.RosterEntryNotFound.ToProblemResult();
        }

        if (await InOpenSwapAsync(db, entry.Id, cancellationToken))
        {
            return WorkforceErrors.EntryInOpenSwap.ToProblemResult();
        }

        var shift = await db.Set<Shift>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ShiftId && s.IsActive, cancellationToken);
        if (shift is null)
        {
            return WorkforceErrors.ShiftUnavailable.ToValidationResult();
        }

        if (shift.Id == entry.ShiftId)
        {
            auditLog.NothingToRecord();
        }
        else
        {
            var before = await db.Set<Shift>().Where(s => s.Id == entry.ShiftId).Select(s => s.Code).SingleAsync(cancellationToken);
            entry.ChangeShift(shift.Id);
            auditLog.Record(
                AuditEvents.Workforce.RosterChanged,
                new AuditOptions
                {
                    Target = new AuditTarget(nameof(RosterEntry), entry.Id),
                    Details = new { change = "shift_changed", employeeId = entry.EmployeeId, dutyDate = entry.DutyDate, from = before, to = shift.Code },
                });
        }

        db.Entry(entry).Property(r => r.Version).OriginalValue = request.Version;
        db.Entry(entry).Property(r => r.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [entry], cancellationToken))[0]);
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> RemoveEntry(
        Guid id,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var entry = await FindManageableAsync(db, permissions, staff, auditLog, id, cancellationToken);
        if (entry is null)
        {
            return WorkforceErrors.RosterEntryNotFound.ToProblemResult();
        }

        if (await InOpenSwapAsync(db, entry.Id, cancellationToken))
        {
            return WorkforceErrors.EntryInOpenSwap.ToProblemResult();
        }

        var shift = await db.Set<Shift>().Where(s => s.Id == entry.ShiftId).Select(s => s.Code).SingleAsync(cancellationToken);
        db.Remove(entry);
        auditLog.Record(
            AuditEvents.Workforce.RosterChanged,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(RosterEntry), entry.Id),
                Details = new { change = "removed", employeeId = entry.EmployeeId, wardId = entry.WardId, shift, dutyDate = entry.DutyDate },
            });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Loads an entry the user may change; out of scope counts as not found (and is logged).</summary>
    private static async Task<RosterEntry?> FindManageableAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        Guid id,
        CancellationToken cancellationToken)
    {
        var entry = await db.Set<RosterEntry>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var reach = await ReachAsync(permissions, staff, Perm.Workforce.RosterManage, cancellationToken);
        if (reach.ReachesWard(entry.WardId))
        {
            return entry;
        }

        await auditLog.RecordOutOfScopeAsync(Perm.Workforce.RosterManage, new AuditTarget(nameof(RosterEntry), entry.Id), cancellationToken);
        return null;
    }

    private static Task<bool> InOpenSwapAsync(AppDbContext db, Guid entryId, CancellationToken cancellationToken) =>
        Swaps.SwapEndpoints.OpenSwapEntryIds(db).AnyAsync(id => id == entryId, cancellationToken);

    private static async Task<StaffReach> ReachAsync(
        ICurrentPermissions permissions,
        IStaffScope staff,
        Permission permission,
        CancellationToken cancellationToken) =>
        await StaffReach.ResolveAsync(await permissions.ScopeForAsync(permission, cancellationToken), staff, cancellationToken);

    /// <summary>Defaults to the current week (Monday to Sunday, hospital time). At most 31 days.</summary>
    private static (DateOnly From, DateOnly To)? Range(DateOnly? from, DateOnly? to, HospitalClock clock)
    {
        var today = clock.Today;
        var start = from ?? today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var end = to ?? start.AddDays(6);
        return end < start || end.DayNumber - start.DayNumber >= MaxDays ? null : (start, end);
    }

    private static async Task<Error?> SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RosterEntryConfiguration.OneShiftPerDay,
        })
        {
            return WorkforceErrors.RosterRaced;
        }
    }

    private static readonly StringComparer NaturalOrder =
        StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, System.Globalization.CompareOptions.NumericOrdering | System.Globalization.CompareOptions.IgnoreCase);

    private static async Task<List<RosterEntryResponse>> ToResponsesAsync(
        AppDbContext db,
        IReadOnlyCollection<RosterEntry> entries,
        CancellationToken cancellationToken)
    {
        var shiftIds = entries.Select(e => e.ShiftId).Distinct().ToList();
        var wardIds = entries.Select(e => e.WardId).Distinct().ToList();
        var shifts = await db.Set<Shift>().Where(s => shiftIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var shiftById = shifts.ToDictionary(
            s => s.Id,
            s => new RosterShift(s.Id, s.Code, s.Name, s.StartsAt, s.EndsAt, s.CrossesMidnight, s.Color));
        var wards = await db.Set<Ward>()
            .Where(w => wardIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => new AssignedWard(w.Id, w.Code, w.Name), cancellationToken);

        return
        [
            .. entries
                .OrderBy(e => e.DutyDate)
                .ThenBy(e => shiftById[e.ShiftId].StartsAt)
                .Select(e => new RosterEntryResponse(e.Id, e.EmployeeId, wards[e.WardId], shiftById[e.ShiftId], e.DutyDate, e.Status, e.Version)),
        ];
    }
}

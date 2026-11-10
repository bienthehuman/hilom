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
using Hilom.Workforce.Features.Assignments;
using Hilom.Workforce.Features.Roster;
using Hilom.Workforce.Persistence;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Workforce.Features.Swaps;

/// <summary>
/// Shift swaps: a nurse offers one of their shifts for a colleague's on the same ward, the colleague accepts
/// or declines, and someone who can approve swaps on that ward approves or rejects. Approval exchanges the
/// shifts in one transaction.
/// </summary>
internal static class SwapEndpoints
{
    /// <summary>How far ahead swap options reach.</summary>
    private const int OptionDays = 31;

    public static async Task<Ok<PagedResponse<SwapResponse>>> ListMySwaps(
        AppDbContext db,
        IStaffScope staff,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } me)
        {
            return TypedResults.Ok(new PagedResponse<SwapResponse>([], 0, pageNumber, size));
        }

        var query = db.Set<ShiftSwapRequest>().AsNoTracking().Where(s => s.RequesterEmployeeId == me || s.TargetEmployeeId == me);
        var total = await query.CountAsync(cancellationToken);
        var swaps = await query.OrderByDescending(s => s.CreatedAt).Skip((pageNumber - 1) * size).Take(size).ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedResponse<SwapResponse>(await ToResponsesAsync(db, swaps, me, cancellationToken), total, pageNumber, size));
    }

    public static async Task<Ok<PagedResponse<SwapResponse>>> ListSwapsForReview(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        int? page,
        int? pageSize,
        SwapStatus? status,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var reach = await StaffReach.ResolveAsync(await permissions.ScopeForAsync(Perm.Workforce.SwapsApprove, cancellationToken), staff, cancellationToken);
        var all = reach.All;
        var wardIds = reach.WardIds;
        var wanted = status ?? SwapStatus.PendingApproval;

        var query = db.Set<ShiftSwapRequest>().AsNoTracking().Where(s => s.Status == wanted && (all || wardIds.Contains(s.WardId)));
        var total = await query.CountAsync(cancellationToken);
        var swaps = await query
            .OrderBy(s => s.RequesterDutyDate < s.TargetDutyDate ? s.RequesterDutyDate : s.TargetDutyDate)
            .ThenBy(s => s.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        var me = await staff.MyEmployeeIdAsync(cancellationToken);
        return TypedResults.Ok(new PagedResponse<SwapResponse>(await ToResponsesAsync(db, swaps, me, cancellationToken), total, pageNumber, size));
    }

    /// <summary>Colleagues' upcoming shifts on the same ward that could be swapped for one of yours.</summary>
    public static async Task<Results<Ok<List<SwapOption>>, ProblemHttpResult, ValidationProblem>> ListSwapOptions(
        Guid entryId,
        AppDbContext db,
        IStaffScope staff,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var mine = await MyUpcomingEntryAsync(db, staff, entryId, today, cancellationToken);
        if (mine is null)
        {
            return WorkforceErrors.SwapNotYours.ToValidationResult();
        }

        var until = today.AddDays(OptionDays);
        var busy = OpenSwapEntryIds(db);
        var entries = await db.Set<RosterEntry>().AsNoTracking()
            .Where(r => r.WardId == mine.WardId
                && r.EmployeeId != mine.EmployeeId
                && r.Status == RosterStatus.Scheduled
                && r.DutyDate >= today && r.DutyDate <= until
                && !busy.Contains(r.Id))
            .OrderBy(r => r.DutyDate)
            .ToListAsync(cancellationToken);

        var shifts = await ShiftsAsync(db, entries.Select(e => e.ShiftId), cancellationToken);
        var employees = await EmployeesAsync(db, entries.Select(e => e.EmployeeId), cancellationToken);
        return TypedResults.Ok(entries
            .Select(e => new SwapOption(e.Id, employees[e.EmployeeId], e.DutyDate, shifts[e.ShiftId]))
            .OrderBy(o => o.DutyDate)
            .ThenBy(o => o.Shift.StartsAt)
            .ThenBy(o => o.Employee.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    public static async Task<Results<Created<SwapResponse>, ProblemHttpResult, ValidationProblem>> RequestSwap(
        RequestSwapRequest request,
        AppDbContext db,
        IStaffScope staff,
        IAuditLog auditLog,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var mine = await MyUpcomingEntryAsync(db, staff, request.RequesterEntryId, today, cancellationToken);
        if (mine is null)
        {
            return WorkforceErrors.SwapNotYours.ToValidationResult();
        }

        var theirs = await db.Set<RosterEntry>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.TargetEntryId, cancellationToken);
        if (theirs is null || theirs.WardId != mine.WardId || theirs.EmployeeId == mine.EmployeeId
            || theirs.Status != RosterStatus.Scheduled || theirs.DutyDate < today)
        {
            return WorkforceErrors.SwapTargetInvalid.ToValidationResult();
        }

        var busy = OpenSwapEntryIds(db);
        if (await busy.AnyAsync(id => id == mine.Id || id == theirs.Id, cancellationToken))
        {
            return WorkforceErrors.EntryInOpenSwap.ToProblemResult();
        }

        var swap = new ShiftSwapRequest(mine, theirs, request.Message);
        if (await CheckFeasibleAsync(db, swap, cancellationToken) is { } infeasible)
        {
            return infeasible.ToProblemResult();
        }

        db.Add(swap);
        auditLog.Record(AuditEvents.Workforce.SwapRequested, Audit(swap, new { requesterDutyDate = swap.RequesterDutyDate, targetDutyDate = swap.TargetDutyDate }));
        if (await SaveAsync(db, cancellationToken) is { } raced)
        {
            return raced.ToProblemResult();
        }

        var response = (await ToResponsesAsync(db, [swap], mine.EmployeeId, cancellationToken))[0];
        return TypedResults.Created($"/api/workforce/swaps/{swap.Id}", response);
    }

    public static Task<Results<Ok<SwapResponse>, ProblemHttpResult>> Accept(
        Guid id, SwapActionRequest request, AppDbContext db, IStaffScope staff, IAuditLog auditLog, TimeProvider time, CancellationToken ct) =>
        ActAsync(id, request, db, staff, auditLog, SwapRole.Target, s => s.Accept(time.GetUtcNow(), request.Note), AuditEvents.Workforce.SwapAccepted, ct);

    public static Task<Results<Ok<SwapResponse>, ProblemHttpResult>> Decline(
        Guid id, SwapActionRequest request, AppDbContext db, IStaffScope staff, IAuditLog auditLog, TimeProvider time, CancellationToken ct) =>
        ActAsync(id, request, db, staff, auditLog, SwapRole.Target, s => s.Decline(time.GetUtcNow(), request.Note), AuditEvents.Workforce.SwapDeclined, ct);

    public static Task<Results<Ok<SwapResponse>, ProblemHttpResult>> Cancel(
        Guid id, SwapActionRequest request, AppDbContext db, IStaffScope staff, IAuditLog auditLog, CancellationToken ct) =>
        ActAsync(id, request, db, staff, auditLog, SwapRole.Requester, s => s.Cancel(), AuditEvents.Workforce.SwapCancelled, ct);

    public static async Task<Results<Ok<SwapResponse>, ProblemHttpResult>> Reject(
        Guid id,
        SwapActionRequest request,
        AppDbContext db,
        IStaffScope staff,
        ICurrentPermissions permissions,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var swap = await ReviewableAsync(db, staff, permissions, auditLog, id, cancellationToken);
        if (swap is null)
        {
            return WorkforceErrors.SwapNotFound.ToProblemResult();
        }

        var rejected = swap.Reject(currentUser.UserId, time.GetUtcNow(), request.Note);
        if (!rejected.IsSuccess)
        {
            return rejected.Error.ToProblemResult();
        }

        auditLog.Record(AuditEvents.Workforce.SwapRejected, Audit(swap, new { note = swap.ReviewNote }));
        db.Entry(swap).Property(s => s.Version).OriginalValue = request.Version;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [swap], await staff.MyEmployeeIdAsync(cancellationToken), cancellationToken))[0]);
    }

    /// <summary>
    /// Exchanges the shifts: both original entries become <c>swapped_out</c> and two new scheduled entries are
    /// created with the people exchanged, in one transaction. Re-checks everything first, since days may have
    /// passed since the request.
    /// </summary>
    public static async Task<Results<Ok<SwapResponse>, ProblemHttpResult>> Approve(
        Guid id,
        SwapActionRequest request,
        AppDbContext db,
        IStaffScope staff,
        ICurrentPermissions permissions,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var swap = await ReviewableAsync(db, staff, permissions, auditLog, id, cancellationToken);
        if (swap is null)
        {
            return WorkforceErrors.SwapNotFound.ToProblemResult();
        }

        if (swap.Status != SwapStatus.PendingApproval)
        {
            return WorkforceErrors.SwapNotWaiting.ToProblemResult();
        }

        var requesterEntry = await db.Set<RosterEntry>().SingleOrDefaultAsync(r => r.Id == swap.RequesterEntryId, cancellationToken);
        var targetEntry = await db.Set<RosterEntry>().SingleOrDefaultAsync(r => r.Id == swap.TargetEntryId, cancellationToken);
        if (requesterEntry is null || targetEntry is null || !swap.Matches(requesterEntry, targetEntry))
        {
            return WorkforceErrors.SwapStale.ToProblemResult();
        }

        if (await CheckFeasibleAsync(db, swap, cancellationToken) is { } infeasible)
        {
            return infeasible.ToProblemResult();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Free both days first, so the one-shift-per-day index never sees two scheduled entries at once.
        requesterEntry.MarkSwappedOut();
        targetEntry.MarkSwappedOut();
        await db.SaveChangesAsync(cancellationToken);

        var targetTakes = new RosterEntry(swap.TargetEmployeeId, swap.WardId, swap.RequesterShiftId, swap.RequesterDutyDate);
        var requesterTakes = new RosterEntry(swap.RequesterEmployeeId, swap.WardId, swap.TargetShiftId, swap.TargetDutyDate);
        db.AddRange(targetTakes, requesterTakes);
        swap.Approve(currentUser.UserId, time.GetUtcNow(), request.Note);
        db.Entry(swap).Property(s => s.Version).OriginalValue = request.Version;
        auditLog.Record(
            AuditEvents.Workforce.SwapApproved,
            Audit(swap, new
            {
                note = swap.ReviewNote,
                swappedOut = new[] { requesterEntry.Id, targetEntry.Id },
                created = new[] { targetTakes.Id, requesterTakes.Id },
            }));

        if (await SaveAsync(db, cancellationToken) is { } raced)
        {
            return raced.ToProblemResult();
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [swap], await staff.MyEmployeeIdAsync(cancellationToken), cancellationToken))[0]);
    }

    /// <summary>Accept, decline (target) or cancel (requester). Anyone else gets 404.</summary>
    private static async Task<Results<Ok<SwapResponse>, ProblemHttpResult>> ActAsync(
        Guid id,
        SwapActionRequest request,
        AppDbContext db,
        IStaffScope staff,
        IAuditLog auditLog,
        SwapRole role,
        Func<ShiftSwapRequest, Result> action,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var swap = await db.Set<ShiftSwapRequest>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        var me = await staff.MyEmployeeIdAsync(cancellationToken);
        if (swap is null)
        {
            return WorkforceErrors.SwapNotFound.ToProblemResult();
        }

        var party = role == SwapRole.Target ? swap.TargetEmployeeId : swap.RequesterEmployeeId;
        if (me != party)
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Workforce.SwapsRequest, new AuditTarget(nameof(ShiftSwapRequest), swap.Id), cancellationToken);
            return WorkforceErrors.SwapNotFound.ToProblemResult();
        }

        var done = action(swap);
        if (!done.IsSuccess)
        {
            return done.Error.ToProblemResult();
        }

        auditLog.Record(auditEvent, Audit(swap, new { note = role == SwapRole.Target ? swap.TargetResponse : null }));
        db.Entry(swap).Property(s => s.Version).OriginalValue = request.Version;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, [swap], me, cancellationToken))[0]);
    }

    /// <summary>A swap on a ward the user may approve swaps for; others are not found (and logged).</summary>
    private static async Task<ShiftSwapRequest?> ReviewableAsync(
        AppDbContext db,
        IStaffScope staff,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        Guid id,
        CancellationToken cancellationToken)
    {
        var swap = await db.Set<ShiftSwapRequest>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (swap is null)
        {
            return null;
        }

        var reach = await StaffReach.ResolveAsync(await permissions.ScopeForAsync(Perm.Workforce.SwapsApprove, cancellationToken), staff, cancellationToken);
        if (reach.ReachesWard(swap.WardId))
        {
            return swap;
        }

        await auditLog.RecordOutOfScopeAsync(Perm.Workforce.SwapsApprove, new AuditTarget(nameof(ShiftSwapRequest), swap.Id), cancellationToken);
        return null;
    }

    private static async Task<RosterEntry?> MyUpcomingEntryAsync(AppDbContext db, IStaffScope staff, Guid entryId, DateOnly today, CancellationToken cancellationToken)
    {
        var me = await staff.MyEmployeeIdAsync(cancellationToken);
        return me is null
            ? null
            : await db.Set<RosterEntry>().AsNoTracking().SingleOrDefaultAsync(
                r => r.Id == entryId && r.EmployeeId == me && r.Status == RosterStatus.Scheduled && r.DutyDate >= today,
                cancellationToken);
    }

    /// <summary>Entries already in an open swap, on either side.</summary>
    internal static IQueryable<Guid> OpenSwapEntryIds(AppDbContext db)
    {
        var open = db.Set<ShiftSwapRequest>().Where(s => s.Status == SwapStatus.PendingTarget || s.Status == SwapStatus.PendingApproval);
        return open.Where(s => s.RequesterEntryId != null).Select(s => s.RequesterEntryId!.Value)
            .Concat(open.Where(s => s.TargetEntryId != null).Select(s => s.TargetEntryId!.Value));
    }

    /// <summary>
    /// After the swap each person must be assigned to the ward on their new day, and have no other shift that
    /// day (the roster's rules).
    /// </summary>
    private static async Task<Error?> CheckFeasibleAsync(AppDbContext db, ShiftSwapRequest swap, CancellationToken cancellationToken)
    {
        var wardCode = await db.Set<Ward>().Where(w => w.Id == swap.WardId).Select(w => w.Code).SingleAsync(cancellationToken);
        var people = await EmployeesAsync(db, [swap.RequesterEmployeeId, swap.TargetEmployeeId], cancellationToken);
        string Name(Guid id) => $"{people[id].FirstName} {people[id].LastName}";

        foreach (var (employeeId, day, givingUp) in new[]
        {
            (swap.TargetEmployeeId, swap.RequesterDutyDate, swap.TargetEntryId),
            (swap.RequesterEmployeeId, swap.TargetDutyDate, swap.RequesterEntryId),
        })
        {
            var assigned = await db.Set<WardAssignment>()
                .AnyAsync(a => a.EmployeeId == employeeId && a.WardId == swap.WardId && a.Period.Contains(day), cancellationToken);
            if (!assigned)
            {
                return WorkforceErrors.NotAssignedToWard(Name(employeeId), wardCode, day);
            }

            var clash = await (
                from r in db.Set<RosterEntry>()
                join s in db.Set<Shift>() on r.ShiftId equals s.Id
                join w in db.Set<Ward>() on r.WardId equals w.Id
                where r.EmployeeId == employeeId && r.DutyDate == day && r.Status == RosterStatus.Scheduled && r.Id != givingUp
                select new { Shift = s.Code, Ward = w.Code })
                .FirstOrDefaultAsync(cancellationToken);
            if (clash is not null)
            {
                return WorkforceErrors.AlreadyRostered(Name(employeeId), clash.Shift, clash.Ward, day);
            }
        }

        return null;
    }

    private static async Task<Error?> SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            return unique.ConstraintName == RosterEntryConfiguration.OneShiftPerDay ? WorkforceErrors.RosterRaced : WorkforceErrors.EntryInOpenSwap;
        }
    }

    private static AuditOptions Audit(ShiftSwapRequest swap, object details) =>
        new()
        {
            Target = new AuditTarget(nameof(ShiftSwapRequest), swap.Id),
            Details = new
            {
                requesterEmployeeId = swap.RequesterEmployeeId,
                targetEmployeeId = swap.TargetEmployeeId,
                wardId = swap.WardId,
                details,
            },
        };

    private static async Task<Dictionary<Guid, AssignedEmployee>> EmployeesAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<Employee>()
            .Where(e => list.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new AssignedEmployee(e.Id, e.EmployeeNo, e.FirstName, e.LastName, e.ProfessionalType), cancellationToken);
    }

    private static async Task<Dictionary<Guid, RosterShift>> ShiftsAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        var shifts = await db.Set<Shift>().Where(s => list.Contains(s.Id)).ToListAsync(cancellationToken);
        return shifts.ToDictionary(s => s.Id, s => new RosterShift(s.Id, s.Code, s.Name, s.StartsAt, s.EndsAt, s.CrossesMidnight, s.Color));
    }

    private static async Task<List<SwapResponse>> ToResponsesAsync(
        AppDbContext db,
        IReadOnlyCollection<ShiftSwapRequest> swaps,
        Guid? me,
        CancellationToken cancellationToken)
    {
        var employees = await EmployeesAsync(db, swaps.SelectMany(s => new[] { s.RequesterEmployeeId, s.TargetEmployeeId }), cancellationToken);
        var shifts = await ShiftsAsync(db, swaps.SelectMany(s => new[] { s.RequesterShiftId, s.TargetShiftId }), cancellationToken);
        var wardIds = swaps.Select(s => s.WardId).Distinct().ToList();
        var wards = await db.Set<Ward>()
            .Where(w => wardIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => new AssignedWard(w.Id, w.Code, w.Name), cancellationToken);
        var reviewerIds = swaps.Select(s => s.ReviewedBy).OfType<Guid>().Distinct().ToList();
        var reviewers = await db.Set<User>()
            .Where(u => reviewerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return
        [
            .. swaps.Select(s => new SwapResponse(
                s.Id,
                s.Status,
                wards[s.WardId],
                new SwapSide(employees[s.RequesterEmployeeId], s.RequesterDutyDate, shifts[s.RequesterShiftId]),
                new SwapSide(employees[s.TargetEmployeeId], s.TargetDutyDate, shifts[s.TargetShiftId]),
                s.Message,
                s.TargetResponse,
                s.TargetRespondedAt,
                s.ReviewNote,
                s.ReviewedBy is { } by ? reviewers.GetValueOrDefault(by) : null,
                s.ReviewedAt,
                s.CreatedAt,
                me == s.RequesterEmployeeId ? SwapRole.Requester : me == s.TargetEmployeeId ? SwapRole.Target : null,
                s.Version)),
        ];
    }
}

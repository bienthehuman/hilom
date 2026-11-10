using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Scheduling.Domain;
using Hilom.Scheduling.Persistence;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Scheduling.Features.Slots;

/// <summary>
/// Physicians' availability: publish slots from a weekly pattern, see them, close them. With
/// <c>scheduling.slots.manage</c> at <c>all</c> (front desk) for any physician; at <c>own</c> (physicians) for
/// themselves only.
/// </summary>
internal static class SlotEndpoints
{
    /// <summary>A slot list covers at most this many days.</summary>
    private const int MaxListDays = 62;

    public static async Task<Ok<List<SlotPhysicianResponse>>> ListPhysicians(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        CancellationToken cancellationToken)
    {
        var reach = await PhysicianReach.ResolveAsync(permissions, staff, cancellationToken);
        var query = db.Set<Employee>().AsNoTracking()
            .Where(e => e.Physician != null && e.Status != EmployeeStatus.Separated);
        if (!reach.All)
        {
            var mine = reach.EmployeeId;
            query = query.Where(e => e.Id == mine);
        }

        var physicians = await (
            from e in query
            join s in db.Set<Specialization>() on e.Physician!.SpecializationId equals s.Id
            orderby e.LastName, e.FirstName
            select new SlotPhysicianResponse(
                e.Id,
                e.FirstName,
                e.LastName,
                s.Name,
                e.Physician!.ConsultationMinutes,
                e.Physician!.AcceptsAppointments))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(physicians);
    }

    /// <summary>A physician's slots between two hospital dates (inclusive), open and closed, in time order.</summary>
    public static async Task<Results<Ok<List<SlotResponse>>, ProblemHttpResult, ValidationProblem>> ListSlots(
        Guid physicianId,
        DateOnly from,
        DateOnly to,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        HospitalClock clock,
        CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxListDays)
        {
            return SchedulingErrors.InvalidPattern("to", $"Choose a range of 1 to {MaxListDays} days.").ToValidationResult();
        }

        if (await FindPhysicianAsync(db, permissions, staff, auditLog, physicianId, cancellationToken) is null)
        {
            return SchedulingErrors.PhysicianNotFound.ToProblemResult();
        }

        var range = AvailabilitySlot.RangeOf(StartOf(from, clock), StartOf(to.AddDays(1), clock));
        var slots = await db.Set<AvailabilitySlot>().AsNoTracking()
            .Where(s => s.PhysicianId == physicianId && s.During.Overlaps(range))
            .OrderBy(s => s.During.LowerBound)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(slots.Select(ToResponse).ToList());
    }

    public static async Task<Results<Ok<PublishSlotsResponse>, ProblemHttpResult, ValidationProblem>> PublishSlots(
        PublishSlotsRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        HospitalClock clock,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var physician = await FindPhysicianAsync(db, permissions, staff, auditLog, request.PhysicianId, cancellationToken);
        if (physician is null)
        {
            return SchedulingErrors.PhysicianNotFound.ToProblemResult();
        }

        if (physician.Physician is not { AcceptsAppointments: true } profile || physician.Status == EmployeeStatus.Separated)
        {
            return SchedulingErrors.PhysicianNotBookable.ToProblemResult();
        }

        var pattern = new WeeklyPattern(
            request.From!.Value,
            request.To!.Value,
            request.Days!.ToHashSet(),
            request.StartTime!.Value,
            request.EndTime!.Value,
            request.SlotMinutes ?? profile.ConsultationMinutes);

        // Today's slots that already started are left out.
        var now = time.GetUtcNow();
        var wanted = pattern.Slots(clock.TimeZone).Where(s => s.StartsAt >= now).ToList();
        if (wanted.Count == 0)
        {
            return SchedulingErrors.InvalidPattern(
                "endTime",
                "These choices make no slots. Check the days, and that the times fit at least one slot.").ToValidationResult();
        }

        if (wanted.Count > WeeklyPattern.MaxSlots)
        {
            return SchedulingErrors.InvalidPattern(
                "to",
                $"That's {wanted.Count} slots. Publish at most {WeeklyPattern.MaxSlots} at a time.").ToValidationResult();
        }

        // Slots that would overlap the physician's existing open slots are skipped, so a pattern can be
        // published again over a partly filled week.
        var window = AvailabilitySlot.RangeOf(wanted[0].StartsAt, wanted[^1].EndsAt);
        var existing = await db.Set<AvailabilitySlot>().AsNoTracking()
            .Where(s => s.PhysicianId == physician.Id && s.Status == SlotStatus.Open && s.During.Overlaps(window))
            .Select(s => s.During)
            .ToListAsync(cancellationToken);
        var created = wanted
            .Where(w => !existing.Any(e => e.LowerBound < w.EndsAt.UtcDateTime && w.StartsAt.UtcDateTime < e.UpperBound))
            .Select(w => AvailabilitySlot.Create(physician.Id, w.StartsAt, w.EndsAt).Value)
            .ToList();
        var skipped = wanted.Count - created.Count;

        if (created.Count == 0)
        {
            auditLog.NothingToRecord();
            return TypedResults.Ok(new PublishSlotsResponse(0, skipped));
        }

        db.AddRange(created);
        auditLog.Record(
            AuditEvents.Scheduling.SlotsPublished,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(Employee), physician.Id),
                Details = new
                {
                    physician = physician.EmployeeNo,
                    from = pattern.From,
                    to = pattern.To,
                    days = pattern.Days.Order().Select(d => d.ToString().ToLowerInvariant()),
                    startTime = pattern.StartTime,
                    endTime = pattern.EndTime,
                    slotMinutes = pattern.SlotMinutes,
                    created = created.Count,
                    skipped,
                },
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: AvailabilitySlotConfiguration.NoOverlap,
        })
        {
            return SchedulingErrors.SlotsRaced.ToProblemResult();
        }

        return TypedResults.Ok(new PublishSlotsResponse(created.Count, skipped));
    }

    public static async Task<Results<Ok<SlotResponse>, ProblemHttpResult>> CloseSlot(
        Guid id,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var slot = await db.Set<AvailabilitySlot>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (slot is null)
        {
            return SchedulingErrors.SlotNotFound.ToProblemResult();
        }

        var reach = await PhysicianReach.ResolveAsync(permissions, staff, cancellationToken);
        if (!reach.Reaches(slot.PhysicianId))
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Scheduling.SlotsManage, Target(slot), cancellationToken);
            return SchedulingErrors.SlotNotFound.ToProblemResult();
        }

        var closed = slot.Close();
        if (!closed.IsSuccess)
        {
            return closed.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Scheduling.SlotClosed,
            new AuditOptions
            {
                Target = Target(slot),
                Details = new { physicianId = slot.PhysicianId, startsAt = slot.StartsAt, endsAt = slot.EndsAt },
            });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(slot));
    }

    /// <summary>The physician, if the caller may manage their slots. Out of scope is logged and treated as not found.</summary>
    private static async Task<Employee?> FindPhysicianAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        Guid physicianId,
        CancellationToken cancellationToken)
    {
        var physician = await db.Set<Employee>().AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == physicianId && e.Physician != null, cancellationToken);
        if (physician is null)
        {
            return null;
        }

        var reach = await PhysicianReach.ResolveAsync(permissions, staff, cancellationToken);
        if (reach.Reaches(physician.Id))
        {
            return physician;
        }

        await auditLog.RecordOutOfScopeAsync(Perm.Scheduling.SlotsManage, new AuditTarget(nameof(Employee), physician.Id), cancellationToken);
        return null;
    }

    /// <summary>Midnight at the start of a hospital date, as a UTC instant.</summary>
    private static DateTimeOffset StartOf(DateOnly day, HospitalClock clock) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.TimeZone), TimeSpan.Zero);

    private static SlotResponse ToResponse(AvailabilitySlot s) => new(s.Id, s.PhysicianId, s.StartsAt, s.EndsAt, s.Status);

    private static AuditTarget Target(AvailabilitySlot slot) => new(nameof(AvailabilitySlot), slot.Id);
}

/// <summary>Whose slots a user may manage: every physician (<c>all</c>), or only themselves (<c>own</c>).</summary>
internal sealed record PhysicianReach(bool All, Guid? EmployeeId)
{
    public static async Task<PhysicianReach> ResolveAsync(ICurrentPermissions permissions, IStaffScope staff, CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(Perm.Scheduling.SlotsManage, cancellationToken);
        if (scope.IsAll)
        {
            return new PhysicianReach(true, null);
        }

        return scope.Includes(PermissionScope.Own)
            ? new PhysicianReach(false, await staff.MyEmployeeIdAsync(cancellationToken))
            : new PhysicianReach(false, null);
    }

    public bool Reaches(Guid physicianId) => All || EmployeeId == physicianId;
}

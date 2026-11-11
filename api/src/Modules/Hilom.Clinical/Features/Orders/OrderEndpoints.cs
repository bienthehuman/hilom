using Hilom.Access.Domain;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Scopes;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Features.Orders;

/// <summary>
/// Physician orders on an encounter (docs/DATA_MODEL.md, physician_orders): the list (<c>clinical.encounters.view</c>),
/// writing one on an open encounter (<c>clinical.orders.write</c>, physicians only) and discontinuing an active one
/// with a reason (<c>clinical.orders.discontinue</c>). Both permissions reach encounters like the other clinical
/// writes: <c>all</c>, or <c>own</c> = the signed-in physician is the attending physician. Order text never
/// reaches the audit log.
/// </summary>
internal static class OrderEndpoints
{
    /// <summary>The encounter's orders, newest first, discontinued ones included. Logged as a view of the encounter.</summary>
    public static async Task<Results<Ok<List<PhysicianOrderResponse>>, ProblemHttpResult>> ListOrders(
        Guid encounterId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.EncountersView, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Clinical.EncounterViewed,
            new AuditOptions { Target = EncounterEndpoints.Target(encounter), PatientId = encounter.PatientId },
            cancellationToken);
        var canDiscontinue = await CanDiscontinueAsync(db, permissions, staff, encounterId, cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, o => o.EncounterId == encounterId, canDiscontinue, cancellationToken));
    }

    /// <summary>Writes an order on an open encounter, ordered by the signed-in physician, now.</summary>
    public static async Task<Results<Created<PhysicianOrderResponse>, ProblemHttpResult>> CreateOrder(
        Guid encounterId,
        CreateOrderRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.OrdersWrite, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        if (encounter.Status != EncounterStatus.Open)
        {
            return ClinicalErrors.EncounterNotOpen.ToProblemResult();
        }

        if (await PhysicianIdAsync(db, staff, cancellationToken) is not { } physicianId)
        {
            return ClinicalErrors.NotAPhysician.ToProblemResult();
        }

        var medication = request.OrderType == OrderType.Medication
            ? new MedicationDetails(request.MedicationName ?? "", request.Dose ?? "", request.Route ?? "", request.Frequency ?? "")
            : null;
        var order = new PhysicianOrder(encounter.Id, encounter.PatientId, physicianId, time.GetUtcNow(), request.OrderType, request.Details, medication);
        db.Add(order);
        auditLog.Record(
            AuditEvents.Clinical.OrderCreated,
            new AuditOptions
            {
                Target = Target(order),
                PatientId = order.PatientId,
                Details = new { encounterId, orderType = order.OrderType, medicationName = order.MedicationName },
            });
        await db.SaveChangesAsync(cancellationToken);

        var canDiscontinue = await CanDiscontinueAsync(db, permissions, staff, encounterId, cancellationToken);
        var created = (await ToResponsesAsync(db, o => o.Id == order.Id, canDiscontinue, cancellationToken))[0];
        return TypedResults.Created($"/api/clinical/encounters/{encounterId}/orders", created);
    }

    /// <summary>Discontinues an active order. The reason is required and is logged; the order's text isn't.</summary>
    public static async Task<Results<Ok<PhysicianOrderResponse>, ProblemHttpResult>> DiscontinueOrder(
        Guid encounterId,
        Guid id,
        DiscontinueOrderRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.OrdersDiscontinue, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        var order = await db.Set<PhysicianOrder>().SingleOrDefaultAsync(o => o.Id == id && o.EncounterId == encounterId, cancellationToken);
        if (order is null)
        {
            return ClinicalErrors.OrderNotFound.ToProblemResult();
        }

        var result = order.Discontinue(currentUser.UserId, request.Reason, time.GetUtcNow());
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemResult();
        }

        auditLog.Record(
            AuditEvents.Clinical.OrderDiscontinued,
            new AuditOptions
            {
                Target = Target(order),
                PatientId = order.PatientId,
                Details = new { encounterId, orderType = order.OrderType, medicationName = order.MedicationName, reason = order.DiscontinueReason },
            });

        // A stale page, or two discontinuations racing, loses on the row version (409).
        db.Entry(order).Property(o => o.Version).OriginalValue = request.Version;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok((await ToResponsesAsync(db, o => o.Id == id, canDiscontinue: true, cancellationToken))[0]);
    }

    /// <summary>The signed-in employee's id when they are a physician; only physicians write orders.</summary>
    internal static async Task<Guid?> PhysicianIdAsync(AppDbContext db, IStaffScope staff, CancellationToken cancellationToken)
    {
        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        return await db.Set<Employee>().AnyAsync(e => e.Id == employeeId && e.Physician != null, cancellationToken) ? employeeId : null;
    }

    /// <summary>Whether the caller may discontinue orders on the encounter (in <c>clinical.orders.discontinue</c> scope).</summary>
    private static async Task<bool> CanDiscontinueAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        var reach = await EncounterReach.ResolveAsync(db, permissions, staff, Perm.Clinical.OrdersDiscontinue, cancellationToken);
        return await reach.Filter(db.Set<Encounter>().Where(e => e.Id == encounterId)).AnyAsync(cancellationToken);
    }

    private static AuditTarget Target(PhysicianOrder order) => new(nameof(PhysicianOrder), order.Id);

    private static async Task<List<PhysicianOrderResponse>> ToResponsesAsync(
        AppDbContext db,
        System.Linq.Expressions.Expression<Func<PhysicianOrder, bool>> filter,
        bool canDiscontinue,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from o in db.Set<PhysicianOrder>().AsNoTracking().Where(filter)
            join e in db.Set<Employee>() on o.OrderedBy equals e.Id
            orderby o.OrderedAt descending, o.Id descending
            select new { Order = o, Name = e.FirstName + " " + e.LastName })
            .ToListAsync(cancellationToken);
        var userIds = rows.Where(r => r.Order.DiscontinuedBy != null).Select(r => r.Order.DiscontinuedBy!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? []
            : await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return
        [
            .. rows.Select(r =>
            {
                var o = r.Order;
                return new PhysicianOrderResponse(
                    o.Id,
                    o.OrderType,
                    o.Status,
                    o.Details,
                    o.MedicationName,
                    o.Dose,
                    o.Route,
                    o.Frequency,
                    o.OrderedAt,
                    r.Name,
                    o.DiscontinuedAt is { } at
                        ? new DiscontinuedResponse(at, o.DiscontinuedBy is { } by ? names.GetValueOrDefault(by) : null, o.DiscontinueReason ?? "")
                        : null,
                    canDiscontinue && o.IsActive,
                    o.Version);
            }),
        ];
    }
}

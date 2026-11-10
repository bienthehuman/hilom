using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Results;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Workforce.Features.Shifts;

/// <summary>Shift definitions (AM, PM, NOC…) used by the duty roster.</summary>
internal static class ShiftEndpoints
{
    public static async Task<Ok<PagedResponse<ShiftResponse>>> ListShifts(
        AppDbContext db,
        int? page,
        int? pageSize,
        ShiftStatusFilter? status,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var query = db.Set<Shift>().AsNoTracking();
        query = status switch
        {
            ShiftStatusFilter.Active => query.Where(s => s.IsActive),
            ShiftStatusFilter.Inactive => query.Where(s => !s.IsActive),
            _ => query,
        };

        var total = await query.CountAsync(cancellationToken);
        var shifts = await query
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StartsAt)
            .ThenBy(s => s.Code)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedResponse<ShiftResponse>([.. shifts.Select(ShiftResponse.From)], total, pageNumber, size));
    }

    public static async Task<Results<Created<ShiftResponse>, ProblemHttpResult, ValidationProblem>> CreateShift(
        CreateShiftRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await CodeTakenAsync(db, request.Code, exceptId: null, cancellationToken))
        {
            return WorkforceErrors.ShiftCodeTaken.ToProblemResult();
        }

        var sortOrder = request.SortOrder ?? await NextSortOrderAsync(db, cancellationToken);
        var created = Shift.Create(request.Code, request.Name, request.StartsAt!.Value, request.EndsAt!.Value, request.Color!.Value, sortOrder);
        if (!created.IsSuccess)
        {
            return created.Error.ToValidationResult();
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/workforce/shifts/{created.Value.Id}", ShiftResponse.From(created.Value));
    }

    public static async Task<Results<Ok<ShiftResponse>, ProblemHttpResult, ValidationProblem>> UpdateShift(
        Guid id,
        UpdateShiftRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var shift = await db.Set<Shift>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return WorkforceErrors.ShiftNotFound.ToProblemResult();
        }

        if (await CodeTakenAsync(db, request.Code, exceptId: id, cancellationToken))
        {
            return WorkforceErrors.ShiftCodeTaken.ToProblemResult();
        }

        var updated = shift.Update(
            request.Code,
            request.Name,
            request.StartsAt!.Value,
            request.EndsAt!.Value,
            request.Color!.Value,
            request.SortOrder ?? shift.SortOrder);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToValidationResult();
        }

        var entry = db.Entry(shift);
        entry.Property(s => s.Version).OriginalValue = request.Version;
        entry.Property(s => s.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ShiftResponse.From(shift));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> SetShiftActive(
        Guid id,
        bool active,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var shift = await db.Set<Shift>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return WorkforceErrors.ShiftNotFound.ToProblemResult();
        }

        if (active)
        {
            shift.Activate();
        }
        else
        {
            shift.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Task<bool> CodeTakenAsync(AppDbContext db, string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = code.Trim();
        return db.Set<Shift>().AnyAsync(s => s.Code == trimmed && s.Id != exceptId, cancellationToken);
    }

    /// <summary>New shifts go last unless an order is given.</summary>
    private static async Task<int> NextSortOrderAsync(AppDbContext db, CancellationToken cancellationToken) =>
        (await db.Set<Shift>().MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0) + 10;
}

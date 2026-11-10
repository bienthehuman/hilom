using Hilom.Facility.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Facility.Features.Wards;

/// <summary>Wards and their beds (docs/DATA_MODEL.md, facility). Deactivated, never deleted.</summary>
internal static class WardEndpoints
{
    public static async Task<Ok<PagedResponse<WardSummaryResponse>>> ListWards(
        AppDbContext db,
        IDepartmentDirectory departments,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        WardType? wardType,
        WardStatusFilter? status,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var (_, descending) = Paging.ParseSort(sort, ["code"], "code");

        var query = db.Set<Ward>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(w => EF.Functions.ILike(w.Code, pattern) || EF.Functions.ILike(w.Name, pattern));
        }

        if (wardType is { } type)
        {
            query = query.Where(w => w.WardType == type);
        }

        query = status switch
        {
            WardStatusFilter.Active => query.Where(w => w.IsActive),
            WardStatusFilter.Inactive => query.Where(w => !w.IsActive),
            _ => query,
        };

        // A hospital has tens of wards, not thousands: sort naturally ("W2" before "W10") and page in memory.
        var wards = await query.ToListAsync(cancellationToken);
        var sorted = descending
            ? wards.OrderByDescending(w => w.Code, WardRules.Natural)
            : wards.OrderBy(w => w.Code, WardRules.Natural);
        var pageOfWards = sorted.Skip((pageNumber - 1) * size).Take(size).ToList();

        var wardIds = pageOfWards.Select(w => w.Id).ToList();
        var beds = await db.Set<Bed>()
            .Where(b => wardIds.Contains(b.WardId))
            .GroupBy(b => b.WardId)
            .Select(g => new { WardId = g.Key, Total = g.Count(), Active = g.Count(b => b.IsActive) })
            .ToDictionaryAsync(g => g.WardId, cancellationToken);
        var departmentInfo = await departments.FindAsync(
            [.. pageOfWards.Select(w => w.DepartmentId).OfType<Guid>().Distinct()],
            cancellationToken);

        var items = pageOfWards
            .Select(w =>
            {
                var counts = beds.GetValueOrDefault(w.Id);
                return new WardSummaryResponse(
                    w.Id,
                    w.Code,
                    w.Name,
                    w.WardType,
                    w.DepartmentId is { } id ? departmentInfo.GetValueOrDefault(id) : null,
                    w.IsActive,
                    w.IsActive ? counts?.Active ?? 0 : 0,
                    counts?.Total ?? 0);
            })
            .ToList();
        return TypedResults.Ok(new PagedResponse<WardSummaryResponse>(items, wards.Count, pageNumber, size));
    }

    public static async Task<Results<Ok<WardResponse>, ProblemHttpResult>> GetWard(
        Guid id,
        AppDbContext db,
        IDepartmentDirectory departments,
        CancellationToken cancellationToken)
    {
        var ward = await db.Set<Ward>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        return ward is null
            ? FacilityErrors.WardNotFound.ToProblemResult()
            : TypedResults.Ok(await ToResponseAsync(db, departments, ward, cancellationToken));
    }

    public static async Task<Results<Created<WardResponse>, ProblemHttpResult, ValidationProblem>> CreateWard(
        CreateWardRequest request,
        AppDbContext db,
        IDepartmentDirectory departments,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        if (await CheckWardAsync(db, departments, request, current: null, cancellationToken) is { } invalid)
        {
            return Fail(invalid);
        }

        var ward = new Ward(request.Code, request.Name, request.WardType!.Value, request.DepartmentId);
        db.Add(ward);
        auditLog.Record(
            AuditEvents.Facility.WardChanged,
            new AuditOptions { Target = WardTarget(ward), Details = new { change = "created", code = ward.Code, name = ward.Name } });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/facility/wards/{ward.Id}", await ToResponseAsync(db, departments, ward, cancellationToken));
    }

    public static async Task<Results<Ok<WardResponse>, ProblemHttpResult, ValidationProblem>> UpdateWard(
        Guid id,
        UpdateWardRequest request,
        AppDbContext db,
        IDepartmentDirectory departments,
        CancellationToken cancellationToken)
    {
        var ward = await db.Set<Ward>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (ward is null)
        {
            return FacilityErrors.WardNotFound.ToProblemResult();
        }

        if (await CheckWardAsync(db, departments, request, ward, cancellationToken) is { } invalid)
        {
            return invalid.Type == ErrorType.Validation ? invalid.ToValidationResult() : invalid.ToProblemResult();
        }

        ward.Update(request.Code, request.Name, request.WardType!.Value, request.DepartmentId);
        await SaveWithVersionAsync(db, ward, request.Version, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, departments, ward, cancellationToken));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> SetWardActive(
        Guid id,
        bool active,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var ward = await db.Set<Ward>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (ward is null)
        {
            return FacilityErrors.WardNotFound.ToProblemResult();
        }

        if (ward.IsActive == active)
        {
            auditLog.NothingToRecord();
            return TypedResults.NoContent();
        }

        if (active)
        {
            ward.Activate();
        }
        else
        {
            ward.Deactivate();
        }

        auditLog.Record(
            AuditEvents.Facility.WardChanged,
            new AuditOptions { Target = WardTarget(ward), Details = new { change = active ? "reactivated" : "deactivated", code = ward.Code } });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Created<BedResponse>, ProblemHttpResult>> AddBed(
        Guid wardId,
        CreateBedRequest request,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var ward = await db.Set<Ward>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == wardId, cancellationToken);
        if (ward is null)
        {
            return FacilityErrors.WardNotFound.ToProblemResult();
        }

        if (await LabelTakenAsync(db, wardId, request.Label, exceptId: null, cancellationToken))
        {
            return FacilityErrors.BedLabelTaken.ToProblemResult();
        }

        var bed = new Bed(wardId, request.Label);
        db.Add(bed);
        auditLog.Record(
            AuditEvents.Facility.BedChanged,
            new AuditOptions { Target = BedTarget(bed), Details = new { change = "added", ward = ward.Code, label = bed.Label } });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/facility/beds/{bed.Id}", ToResponse(bed));
    }

    public static async Task<Results<Ok<BedResponse>, ProblemHttpResult>> UpdateBed(
        Guid id,
        UpdateBedRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var bed = await db.Set<Bed>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bed is null)
        {
            return FacilityErrors.BedNotFound.ToProblemResult();
        }

        if (await LabelTakenAsync(db, bed.WardId, request.Label, exceptId: id, cancellationToken))
        {
            return FacilityErrors.BedLabelTaken.ToProblemResult();
        }

        bed.Relabel(request.Label);
        var entry = db.Entry(bed);
        entry.Property(b => b.Version).OriginalValue = request.Version;
        entry.Property(b => b.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(bed));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> SetBedActive(
        Guid id,
        bool active,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var bed = await db.Set<Bed>().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bed is null)
        {
            return FacilityErrors.BedNotFound.ToProblemResult();
        }

        if (bed.IsActive == active)
        {
            auditLog.NothingToRecord();
            return TypedResults.NoContent();
        }

        if (active)
        {
            bed.Activate();
        }
        else
        {
            bed.Deactivate();
        }

        auditLog.Record(
            AuditEvents.Facility.BedChanged,
            new AuditOptions { Target = BedTarget(bed), Details = new { change = active ? "reactivated" : "deactivated", label = bed.Label } });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Unique code, and a new or changed department must exist and be active.</summary>
    private static async Task<Error?> CheckWardAsync(
        AppDbContext db,
        IDepartmentDirectory departments,
        IWardFields request,
        Ward? current,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var exceptId = current?.Id;
        if (await db.Set<Ward>().AnyAsync(w => w.Code == code && w.Id != exceptId, cancellationToken))
        {
            return FacilityErrors.WardCodeTaken;
        }

        if (request.DepartmentId is { } departmentId && departmentId != current?.DepartmentId)
        {
            var found = await departments.FindAsync([departmentId], cancellationToken);
            if (!found.TryGetValue(departmentId, out var department) || !department.IsActive)
            {
                return FacilityErrors.UnknownDepartment;
            }
        }

        return null;
    }

    private static Results<Created<WardResponse>, ProblemHttpResult, ValidationProblem> Fail(Error error) =>
        error.Type == ErrorType.Validation ? error.ToValidationResult() : error.ToProblemResult();

    private static Task<bool> LabelTakenAsync(AppDbContext db, Guid wardId, string label, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = label.Trim();
        return db.Set<Bed>().AnyAsync(b => b.WardId == wardId && b.Label == trimmed && b.Id != exceptId, cancellationToken);
    }

    private static Task<int> SaveWithVersionAsync(AppDbContext db, Ward ward, uint version, CancellationToken cancellationToken)
    {
        var entry = db.Entry(ward);
        entry.Property(w => w.Version).OriginalValue = version;
        entry.Property(w => w.UpdatedAt).IsModified = true;
        return db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<WardResponse> ToResponseAsync(
        AppDbContext db,
        IDepartmentDirectory departments,
        Ward ward,
        CancellationToken cancellationToken)
    {
        var beds = await db.Set<Bed>().AsNoTracking().Where(b => b.WardId == ward.Id).ToListAsync(cancellationToken);
        var department = ward.DepartmentId is { } id
            ? (await departments.FindAsync([id], cancellationToken)).GetValueOrDefault(id)
            : null;
        return new WardResponse(
            ward.Id,
            ward.Code,
            ward.Name,
            ward.WardType,
            department,
            ward.IsActive,
            ward.IsActive ? beds.Count(b => b.IsActive) : 0,
            ward.Version,
            [.. beds.OrderBy(b => b.Label, WardRules.Natural).Select(ToResponse)]);
    }

    private static BedResponse ToResponse(Bed bed) => new(bed.Id, bed.Label, bed.IsActive, bed.Version);

    private static AuditTarget WardTarget(Ward ward) => new(nameof(Ward), ward.Id);

    private static AuditTarget BedTarget(Bed bed) => new(nameof(Bed), bed.Id);
}

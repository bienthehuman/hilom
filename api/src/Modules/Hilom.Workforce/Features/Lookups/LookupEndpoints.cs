using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.Workforce.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Workforce.Features.Lookups;

/// <summary>
/// The same list/create/edit/deactivate endpoints for every lookup (departments, positions, specializations).
/// Any signed-in user can read them, since forms and filters across the app need them; changing them needs
/// <c>workforce.employees.manage</c>.
/// </summary>
internal static class LookupEndpoints
{
    // Lookups are reference data, not records about a person or patient. Every change is still in the
    // audit log as a data change with before/after values (docs/AUDIT.md).
    private const string NotAuditedReason = "Reference data; edits are recorded as data changes";

    private static readonly string[] SortFields = ["name"];

    /// <param name="route">Plural kebab-case route, e.g. "departments".</param>
    public static void MapLookup<T>(this IEndpointRouteBuilder endpoints, string route)
        where T : Lookup, ILookupKind<T>
    {
        var singular = typeof(T).Name;
        var plural = singular + "s";
        var group = endpoints.MapGroup($"/{route}");

        group.MapGet("", List<T>)
            .RequireSignedIn()
            .WithName($"List{plural}")
            .WithSummary($"{plural}, paged, with search and active filter");
        group.MapPost("", Create<T>)
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .WithValidation<CreateLookupRequest>()
            .NotAudited(NotAuditedReason)
            .WithName($"Create{singular}");
        group.MapPut("/{id:guid}", Update<T>)
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .WithValidation<UpdateLookupRequest>()
            .NotAudited(NotAuditedReason)
            .WithName($"Update{singular}");
        group.MapPost("/{id:guid}/deactivate", (Guid id, AppDbContext db, CancellationToken ct) => SetActive<T>(id, false, db, ct))
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .NotAudited(NotAuditedReason)
            .WithName($"Deactivate{singular}")
            .WithSummary("Hide from pickers for new records; existing records keep it");
        group.MapPost("/{id:guid}/activate", (Guid id, AppDbContext db, CancellationToken ct) => SetActive<T>(id, true, db, ct))
            .RequirePermission(Perm.Workforce.EmployeesManage)
            .NotAudited(NotAuditedReason)
            .WithName($"Activate{singular}");
    }

    private static async Task<Ok<PagedResponse<LookupResponse>>> List<T>(
        AppDbContext db,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        LookupStatusFilter? status,
        CancellationToken cancellationToken)
        where T : Lookup, ILookupKind<T>
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var (_, descending) = Paging.ParseSort(sort, SortFields, "name");

        var query = db.Set<T>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(l => EF.Functions.ILike(l.Name, pattern));
        }

        query = status switch
        {
            LookupStatusFilter.Active => query.Where(l => l.IsActive),
            LookupStatusFilter.Inactive => query.Where(l => !l.IsActive),
            _ => query,
        };

        query = descending ? query.OrderByDescending(l => l.Name) : query.OrderBy(l => l.Name);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(l => new LookupResponse(l.Id, l.Name, l.IsActive, l.Version))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedResponse<LookupResponse>(items, total, pageNumber, size));
    }

    private static async Task<Results<Created<LookupResponse>, ProblemHttpResult>> Create<T>(
        CreateLookupRequest request,
        AppDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
        where T : Lookup, ILookupKind<T>
    {
        if (await NameTakenAsync<T>(db, request.Name, exceptId: null, cancellationToken))
        {
            return NameTaken<T>().ToProblemResult();
        }

        var lookup = T.Create(request.Name);
        db.Add(lookup);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"{http.Request.Path}/{lookup.Id}", LookupResponse.From(lookup));
    }

    private static async Task<Results<Ok<LookupResponse>, ProblemHttpResult>> Update<T>(
        Guid id,
        UpdateLookupRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
        where T : Lookup, ILookupKind<T>
    {
        var lookup = await db.Set<T>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lookup is null)
        {
            return NotFound<T>().ToProblemResult();
        }

        if (await NameTakenAsync<T>(db, request.Name, exceptId: id, cancellationToken))
        {
            return NameTaken<T>().ToProblemResult();
        }

        lookup.Rename(request.Name);

        // Always write the row so a stale version is caught even when the name didn't change.
        var entry = db.Entry(lookup);
        entry.Property(l => l.Version).OriginalValue = request.Version;
        entry.Property(l => l.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(LookupResponse.From(lookup));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetActive<T>(
        Guid id,
        bool active,
        AppDbContext db,
        CancellationToken cancellationToken)
        where T : Lookup, ILookupKind<T>
    {
        var lookup = await db.Set<T>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lookup is null)
        {
            return NotFound<T>().ToProblemResult();
        }

        if (active)
        {
            lookup.Activate();
        }
        else
        {
            lookup.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Task<bool> NameTakenAsync<T>(AppDbContext db, string name, Guid? exceptId, CancellationToken cancellationToken)
        where T : Lookup
    {
        var trimmed = name.Trim();
        return db.Set<T>().AnyAsync(l => l.Name == trimmed && l.Id != exceptId, cancellationToken);
    }

    private static Error NotFound<T>()
        where T : Lookup, ILookupKind<T> =>
        Error.NotFound("lookup_not_found", $"This {T.Noun} doesn't exist.");

    private static Error NameTaken<T>()
        where T : Lookup, ILookupKind<T> =>
        Error.Conflict("lookup_name_taken", $"Another {T.Noun} already has this name.");
}

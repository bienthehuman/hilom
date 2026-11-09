using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Roles;

/// <summary>The permission catalog and role management (docs/RBAC.md).</summary>
internal static class RoleEndpoints
{
    public static Ok<IReadOnlyList<PermissionGroupResponse>> GetPermissionCatalog() =>
        TypedResults.Ok<IReadOnlyList<PermissionGroupResponse>>(
        [
            .. Perm.All
                .GroupBy(p => p.Group)
                .Select(group => new PermissionGroupResponse(
                    group.Key,
                    [.. group.Select(p => new PermissionResponse(p.Key, p.Description, p.AllowedScopes))])),
        ]);

    public static async Task<Ok<List<RoleSummaryResponse>>> ListRoles(AppDbContext db, CancellationToken cancellationToken)
    {
        var roles = await db.Set<Role>()
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => r.Name)
            .Select(r => new RoleSummaryResponse(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                db.Set<UserRole>().Count(ur => ur.RoleId == r.Id)))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(roles);
    }

    public static async Task<Results<Ok<RoleResponse>, ProblemHttpResult>> GetRole(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return role is null
            ? Problem(AccessErrors.RoleNotFound)
            : TypedResults.Ok(RoleResponse.From(role));
    }

    public static async Task<Results<Created<RoleResponse>, ProblemHttpResult, ValidationProblem>> CreateRole(
        CreateRoleRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await NameTakenAsync(db, request.Name, exceptId: null, cancellationToken))
        {
            return Problem(AccessErrors.RoleNameTaken);
        }

        var created = Role.Create(request.Name, request.Description ?? "", ToGrants(request.Grants));
        if (!created.IsSuccess)
        {
            return Validation(created.Error);
        }

        db.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/access/roles/{created.Value.Id}", RoleResponse.From(created.Value));
    }

    public static async Task<Results<Ok<RoleResponse>, ProblemHttpResult, ValidationProblem>> UpdateRole(
        Guid id,
        UpdateRoleRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problem(AccessErrors.RoleNotFound);
        }

        if (role.IsSystem)
        {
            return Problem(AccessErrors.SystemRoleProtected);
        }

        if (await NameTakenAsync(db, request.Name, exceptId: id, cancellationToken))
        {
            return Problem(AccessErrors.RoleNameTaken);
        }

        role.Rename(request.Name, request.Description ?? "");
        var granted = role.SetGrants(ToGrants(request.Grants));
        if (!granted.IsSuccess)
        {
            return Validation(granted.Error);
        }

        // Always update the role row, even if only grants changed, so the version check runs.
        var entry = db.Entry(role);
        entry.Property(r => r.Version).OriginalValue = request.Version;
        entry.Property(r => r.UpdatedAt).IsModified = true;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshHoldersAsync(db, role.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(RoleResponse.From(role));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteRole(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problem(AccessErrors.RoleNotFound);
        }

        if (role.IsSystem)
        {
            return Problem(AccessErrors.SystemRoleProtected);
        }

        if (await db.Set<UserRole>().AnyAsync(ur => ur.RoleId == id, cancellationToken))
        {
            return Problem(AccessErrors.RoleInUse);
        }

        db.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static IEnumerable<RoleGrant> ToGrants(IReadOnlyList<GrantDto> grants) =>
        grants.Select(g => new RoleGrant(g.Permission, g.Scope));

    private static Task<bool> NameTakenAsync(AppDbContext db, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        return db.Set<Role>().AnyAsync(r => r.Name == trimmed && r.Id != exceptId, cancellationToken);
    }

    /// <summary>Everyone holding the role reloads their permissions on their next request.</summary>
    internal static Task<int> RefreshHoldersAsync(AppDbContext db, Guid roleId, CancellationToken cancellationToken) =>
        db.Set<User>()
            .Where(u => db.Set<UserRole>().Any(ur => ur.RoleId == roleId && ur.UserId == u.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.PermissionsVersion, u => u.PermissionsVersion + 1), cancellationToken);

    private static ProblemHttpResult Problem(SharedKernel.Results.Error error) => error.ToProblemResult();

    private static ValidationProblem Validation(SharedKernel.Results.Error error) => error.ToValidationResult();
}

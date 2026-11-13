using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.DemoData;

public sealed record DemoAccountResponse(string Role, string Email, string DisplayName, string Description, bool Portal);

public sealed record DemoAccountsResponse(bool Enabled, string? Password, IReadOnlyList<DemoAccountResponse> Accounts);

public static class DemoEndpoints
{
    /// <summary>
    /// What the sign-in pages need to offer one-click demo logins. Always mapped, so the API contract is the same with demo
    /// mode on or off; with it off the answer is "not enabled" and nothing else.
    /// </summary>
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/demo/accounts", ListAccounts)
            .WithTags("Demo")
            .WithName("ListDemoAccounts")
            .AllowAnonymous();
        return api;
    }

    private static async Task<Ok<DemoAccountsResponse>> ListAccounts(
        AppDbContext db,
        IOptions<DemoOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return TypedResults.Ok(new DemoAccountsResponse(false, null, []));
        }

        var rows = await (
            from user in db.Set<User>().AsNoTracking()
            join userRole in db.Set<UserRole>() on user.Id equals userRole.UserId
            join role in db.Set<Role>() on userRole.RoleId equals role.Id
            where EF.Functions.ILike(user.Email, "%" + DemoAccountCatalog.EmailDomain) && user.Status == UserStatus.Active
            select new { user.Email, user.DisplayName, user.AccountType, Role = role.Name })
            .ToListAsync(cancellationToken);

        var accounts = rows
            .OrderBy(r => DemoAccountCatalog.OrderOf(r.Role))
            .Select(r => new DemoAccountResponse(r.Role, r.Email, r.DisplayName, DemoAccountCatalog.Describe(r.Role), r.AccountType == AccountType.Portal))
            .ToList();
        return TypedResults.Ok(new DemoAccountsResponse(true, options.Value.Password, accounts));
    }
}

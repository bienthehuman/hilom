using System.Security.Claims;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.Access.Sessions;

/// <summary>
/// Runs on every request with a session cookie. Ends the session if the user was deleted or disabled,
/// or if their security stamp changed (password changed, signed out everywhere).
/// </summary>
internal static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userIdText = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stampText = principal?.FindFirstValue(Session.SecurityStampClaim);

        if (!Guid.TryParse(userIdText, out var userId) || !Guid.TryParse(stampText, out var stamp))
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Status, u.SecurityStamp })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || user.Status != UserStatus.Active || user.SecurityStamp != stamp)
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(Session.Scheme);
    }
}

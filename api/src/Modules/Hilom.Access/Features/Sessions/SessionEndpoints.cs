using Hilom.Access.Domain;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Sessions;

/// <summary>Antiforgery token, sign-out and the current user.</summary>
internal static class SessionEndpoints
{
    /// <summary>Issues the XSRF-TOKEN cookie. The web app calls this once on startup.</summary>
    public static NoContent GetAntiforgeryToken(HttpContext httpContext, IAntiforgery antiforgery)
    {
        AntiforgeryCookie.Refresh(httpContext, antiforgery);
        return TypedResults.NoContent();
    }

    /// <summary>Ends the session. Safe to call when already signed out.</summary>
    public static async Task<NoContent> Logout(HttpContext httpContext, IAntiforgery antiforgery, IAuditLog auditLog)
    {
        // Recorded before signing out, so the entry still knows who it was.
        await auditLog.RecordNowAsync(AuditEvents.Auth.Logout, null, httpContext.RequestAborted);
        await httpContext.SignOutAsync(Session.Scheme);
        httpContext.User = new();
        AntiforgeryCookie.Refresh(httpContext, antiforgery);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<CurrentUserResponse>, UnauthorizedHttpResult>> GetCurrentUser(
        ICurrentUser currentUser,
        ICurrentPermissions permissions,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>()
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);

        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(CurrentUserResponse.From(user, await permissions.GetAsync(cancellationToken)));
    }
}

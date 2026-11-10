using FluentValidation;
using Hilom.Access.Domain;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Sessions;

public sealed record RegisterRequest(string DisplayName, string Email, string Password);

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.DisplayName).NotEmpty().WithMessage("Enter your name.").MaximumLength(200);
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Enter your email.")
            .EmailAddress().WithMessage("Enter an email address like name@example.com.")
            .MaximumLength(320);
        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(12).WithMessage("Use at least 12 characters.")
            .MaximumLength(128);
    }
}

/// <summary>
/// Patients and families create their own portal account and are signed in straight away. The account gets
/// only the portal role, which reaches nothing until staff verify a link to a patient (docs/adr/0003).
/// </summary>
internal static class Register
{
    public static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> Handle(
        RegisterRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        IPermissionStore permissions,
        IAuditLog auditLog,
        IAntiforgery antiforgery,
        TimeProvider time,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (await PortalRoleGuard.PortalRoleIdAsync(db, cancellationToken) is not { } portalRoleId)
        {
            return AccessErrors.SelfRegistrationUnavailable.ToProblemResult();
        }

        var email = request.Email.Trim();
        if (await db.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return AccessErrors.PortalEmailTaken.ToProblemResult();
        }

        var user = new User(email, request.DisplayName, AccountType.Portal);
        user.ChangePassword(hasher.HashPassword(user, request.Password));
        db.Add(user);
        db.Add(new UserRole(user.Id, portalRoleId, assignedBy: null, time.GetUtcNow()));
        auditLog.Record(
            AuditEvents.Auth.AccountRegistered,
            new AuditOptions
            {
                ActorId = user.Id,
                Target = new AuditTarget(nameof(User), user.Id),
                Details = new { email = user.Email, displayName = user.DisplayName },
            });

        // Two sign-ups with one email at once: the unique index lets only one through (409 duplicate).
        await db.SaveChangesAsync(cancellationToken);

        var principal = Session.CreatePrincipal(user);
        await httpContext.SignInAsync(Session.Scheme, principal);
        httpContext.User = principal;
        AntiforgeryCookie.Refresh(httpContext, antiforgery);

        return TypedResults.Ok(CurrentUserResponse.From(user, await permissions.GetPermissionsAsync(user.Id, cancellationToken)));
    }
}

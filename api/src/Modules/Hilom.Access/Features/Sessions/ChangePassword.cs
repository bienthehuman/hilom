using FluentValidation;
using Hilom.Access.Domain;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Access.Features.Sessions;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().WithMessage("Enter your current password.");
        RuleFor(r => r.NewPassword)
            .NotEmpty().WithMessage("Choose a new password.")
            .MinimumLength(12).WithMessage("Use at least 12 characters.")
            .MaximumLength(128)
            .NotEqual(r => r.CurrentPassword).WithMessage("Choose a password different from your current one.");
    }
}

/// <summary>
/// The signed-in user replaces their own password: required after an administrator set a temporary one,
/// and available any time. Ends every other session and keeps this one.
/// </summary>
internal static class ChangePassword
{
    private static readonly Error WrongCurrentPassword = Error.Validation(
        "wrong_current_password",
        "Your current password isn't right.",
        new Dictionary<string, string[]> { ["currentPassword"] = ["Your current password isn't right."] });

    public static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, UnauthorizedHttpResult>> Handle(
        ChangePasswordRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        ICurrentUser currentUser,
        IPermissionStore permissions,
        IAuditLog auditLog,
        IAntiforgery antiforgery,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            // Someone at an unlocked screen guessing the password. The endpoint shares the sign-in rate limit.
            await auditLog.RecordNowAsync(
                AuditEvents.Auth.PasswordChanged,
                new AuditOptions
                {
                    Outcome = AuditOutcome.Failed,
                    Target = new AuditTarget(nameof(User), user.Id),
                    Details = new { reason = "wrong_current_password" },
                },
                cancellationToken);
            return WrongCurrentPassword.ToValidationResult();
        }

        var wasTemporary = user.MustChangePassword;
        user.ChangePassword(hasher.HashPassword(user, request.NewPassword));
        auditLog.Record(
            AuditEvents.Auth.PasswordChanged,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(User), user.Id),
                Details = new { replacedTemporaryPassword = wasTemporary },
            });
        await db.SaveChangesAsync(cancellationToken);

        // The new security stamp ends every session, this one included, so start this one again.
        var principal = Session.CreatePrincipal(user);
        await httpContext.SignInAsync(Session.Scheme, principal);
        httpContext.User = principal;
        AntiforgeryCookie.Refresh(httpContext, antiforgery);

        return TypedResults.Ok(CurrentUserResponse.From(user, await permissions.GetPermissionsAsync(user.Id, cancellationToken)));
    }
}

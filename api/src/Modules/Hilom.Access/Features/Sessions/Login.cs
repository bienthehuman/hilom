using FluentValidation;
using Hilom.Access.Domain;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Auditing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.Access.Features.Sessions;

public sealed record LoginRequest(string Email, string Password);

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Enter your email.")
            .EmailAddress().WithMessage("Enter an email address like name@example.com.");
        RuleFor(r => r.Password).NotEmpty().WithMessage("Enter your password.");
    }
}

/// <summary>Signs in with email and password and starts a cookie session.</summary>
internal static class Login
{
    // Checked when the email is unknown, so the response takes as long as a real check
    // and doesn't reveal which emails have accounts.
    private static readonly string UnknownUserHash =
        new PasswordHasher<User>().HashPassword(null!, "unknown-user-placeholder");

    public static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> Handle(
        LoginRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        IPermissionStore permissions,
        IAuditLog auditLog,
        IAntiforgery antiforgery,
        TimeProvider time,
        IOptions<AccessOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var email = request.Email.Trim();
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Failed attempts are recorded with the email typed, never the password, and no actor:
        // whoever typed it may not be the account's owner.
        Task FailedAsync(string reason, User? account) =>
            auditLog.RecordNowAsync(
                AuditEvents.Auth.LoginFailed,
                new AuditOptions
                {
                    Outcome = AuditOutcome.Failed,
                    AttemptedIdentifier = email,
                    Target = account is null ? null : new AuditTarget(nameof(User), account.Id),
                    Details = new { reason },
                },
                cancellationToken);

        if (user is null)
        {
            hasher.VerifyHashedPassword(null!, UnknownUserHash, request.Password);
            await FailedAsync("unknown_email", null);
            return InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            await FailedAsync("locked_out", user);
            return LockedOut(user.LockoutEndsAt!.Value - now);
        }

        var check = user.PasswordHash.Length == 0
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (check == PasswordVerificationResult.Failed)
        {
            var lockedUntil = await RecordFailedSignInAsync(db, user.Id, now, options.Value.Lockout, cancellationToken);
            if (lockedUntil > now)
            {
                auditLog.Record(
                    AuditEvents.Auth.AccountLockedOut,
                    new AuditOptions
                    {
                        Outcome = AuditOutcome.Failed,
                        AttemptedIdentifier = email,
                        Target = new AuditTarget(nameof(User), user.Id),
                        Details = new { lockedUntil },
                    });
            }

            await FailedAsync("wrong_password", user);
            return lockedUntil > now ? LockedOut(options.Value.Lockout.Duration) : InvalidCredentials();
        }

        // A disabled account gets the same answer as a wrong password.
        if (user.Status != UserStatus.Active)
        {
            await FailedAsync("disabled", user);
            return InvalidCredentials();
        }

        await RecordSuccessfulSignInAsync(
            db,
            user.Id,
            now,
            check == PasswordVerificationResult.SuccessRehashNeeded ? hasher.HashPassword(user, request.Password) : null,
            cancellationToken);

        await auditLog.RecordNowAsync(
            AuditEvents.Auth.LoginSucceeded,
            new AuditOptions { ActorId = user.Id, Target = new AuditTarget(nameof(User), user.Id) },
            cancellationToken);

        var principal = Session.CreatePrincipal(user);
        await httpContext.SignInAsync(Session.Scheme, principal);

        // Antiforgery tokens belong to a user, so issue new ones for the signed-in session.
        httpContext.User = principal;
        AntiforgeryCookie.Refresh(httpContext, antiforgery);

        return TypedResults.Ok(CurrentUserResponse.From(user, await permissions.GetPermissionsAsync(user.Id, cancellationToken)));
    }

    // Sign-in bookkeeping uses single UPDATE statements rather than load-change-save. Several sign-ins to one
    // account at the same moment (a ward PC and a phone) then apply one after another in the database, instead
    // of tripping the row's edit-conflict check. Changes nothing else, so it doesn't end other sessions.

    private static Task<int> RecordSuccessfulSignInAsync(
        AppDbContext db,
        Guid userId,
        DateTimeOffset now,
        string? upgradedHash,
        CancellationToken cancellationToken) =>
        db.Set<User>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                set =>
                {
                    set.SetProperty(u => u.FailedLoginCount, 0)
                        .SetProperty(u => u.LockoutEndsAt, (DateTimeOffset?)null)
                        .SetProperty(u => u.LastLoginAt, now)
                        .SetProperty(u => u.UpdatedAt, now);
                    if (upgradedHash is not null)
                    {
                        set.SetProperty(u => u.PasswordHash, upgradedHash);
                    }
                },
                cancellationToken);

    /// <summary>
    /// Counts a wrong password; on reaching the limit, locks the account and resets the count.
    /// Both SET expressions read the row's values from before this statement. Returns the lockout end, if any.
    /// </summary>
    private static async Task<DateTimeOffset?> RecordFailedSignInAsync(
        AppDbContext db,
        Guid userId,
        DateTimeOffset now,
        LockoutPolicy policy,
        CancellationToken cancellationToken)
    {
        var limit = policy.MaxFailedAttempts;
        var lockUntil = now + policy.Duration;

        await db.Set<User>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.LockoutEndsAt, u => u.FailedLoginCount + 1 >= limit ? lockUntil : u.LockoutEndsAt)
                    .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1 >= limit ? 0 : u.FailedLoginCount + 1)
                    .SetProperty(u => u.UpdatedAt, now),
                cancellationToken);

        return await db.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.LockoutEndsAt)
            .SingleAsync(cancellationToken);
    }

    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Sign-in failed",
            detail: "Email or password is incorrect.",
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials" });

    private static ProblemHttpResult LockedOut(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Account temporarily locked",
            detail: $"Too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "account_locked",
                ["retryAfterMinutes"] = minutes,
            });
    }
}

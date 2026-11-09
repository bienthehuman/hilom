using FluentValidation;
using Hilom.Access.Domain;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Persistence;
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
        IAntiforgery antiforgery,
        TimeProvider time,
        IOptions<AccessOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var email = request.Email.Trim();
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            hasher.VerifyHashedPassword(null!, UnknownUserHash, request.Password);
            return InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            return LockedOut(user.LockoutEndsAt!.Value - now);
        }

        var check = user.PasswordHash.Length == 0
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (check == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(now, options.Value.Lockout);
            await db.SaveChangesAsync(cancellationToken);
            return user.IsLockedOut(now) ? LockedOut(options.Value.Lockout.Duration) : InvalidCredentials();
        }

        // A disabled account gets the same answer as a wrong password.
        if (user.Status != UserStatus.Active)
        {
            return InvalidCredentials();
        }

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(hasher.HashPassword(user, request.Password));
        }

        user.RecordSuccessfulLogin(now);
        await db.SaveChangesAsync(cancellationToken);

        var principal = Session.CreatePrincipal(user);
        await httpContext.SignInAsync(Session.Scheme, principal);

        // Antiforgery tokens belong to a user, so issue new ones for the signed-in session.
        httpContext.User = principal;
        AntiforgeryCookie.Refresh(httpContext, antiforgery);

        return TypedResults.Ok(CurrentUserResponse.From(user));
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

using FluentValidation;
using Hilom.Access.Domain;
using Hilom.Access.Features.Sessions;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Email;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.Access.Features.EmailVerification;

public sealed record VerifyEmailRequest(string Code);

internal sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(r => r.Code)
            .NotEmpty().WithMessage("Enter the code from the email.")
            .Matches("^[0-9]{6}$").WithMessage("The code is 6 digits.");
    }
}

/// <summary>A self-registered portal user confirms their email with a code sent to it.</summary>
internal static class EmailVerificationEndpoints
{
    private static readonly Error WrongCode = Error.Validation(
        "email_code_wrong",
        "That code isn't right. Check the email and try again.",
        new Dictionary<string, string[]> { ["code"] = ["That code isn't right. Check the email and try again."] });

    /// <summary>Sends a new code, replacing the last one. At most one per minute.</summary>
    public static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> SendCode(
        AppDbContext db,
        ICurrentUser currentUser,
        IEmailSender email,
        IOptions<HospitalOptions> hospital,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.MustVerifyEmail)
        {
            return AccessErrors.EmailAlreadyVerified.ToProblemResult();
        }

        var now = time.GetUtcNow();
        if (user.EmailCodeSentAt is { } sentAt && now - sentAt < EmailCodes.ResendAfter)
        {
            var wait = (int)Math.Ceiling((EmailCodes.ResendAfter - (now - sentAt)).TotalSeconds);
            return TypedResults.Problem(
                detail: $"A code was sent a moment ago. You can ask for another in {wait} seconds.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = AccessErrors.EmailCodeCooldown,
                    ["retryAfterSeconds"] = wait,
                });
        }

        var message = EmailCodes.Issue(user, now, hospital.Value.Name);
        auditLog.Record(
            AuditEvents.Auth.EmailCodeSent,
            new AuditOptions { Target = new AuditTarget(nameof(User), user.Id), Details = new { email = user.Email } });

        // Send before saving: if the email can't go out, the earlier code still works and the user can retry.
        try
        {
            await email.SendAsync(message, cancellationToken);
        }
        catch (EmailNotSentException)
        {
            return TypedResults.Problem(
                detail: AccessErrors.EmailUnavailable.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = AccessErrors.EmailUnavailable.Code });
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Checks the code. On success the account may request patient links.</summary>
    public static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> Verify(
        VerifyEmailRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        IPermissionStore permissions,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.MustVerifyEmail)
        {
            return TypedResults.Ok(CurrentUserResponse.From(user, await permissions.GetPermissionsAsync(user.Id, cancellationToken)));
        }

        var now = time.GetUtcNow();
        if (user.EmailCodeHash is null || user.EmailCodeAttempts >= EmailCodes.MaxAttempts)
        {
            await RecordFailureAsync(auditLog, user, "no_usable_code", cancellationToken);
            return AccessErrors.EmailCodeUsedUp.ToProblemResult();
        }

        if (user.EmailCodeExpiresAt <= now)
        {
            await RecordFailureAsync(auditLog, user, "expired", cancellationToken);
            return AccessErrors.EmailCodeExpired.ToProblemResult();
        }

        if (!EmailCodes.Matches(user, request.Code))
        {
            user.EmailCodeFailed();
            auditLog.Record(
                AuditEvents.Auth.EmailVerified,
                new AuditOptions
                {
                    Outcome = AuditOutcome.Failed,
                    Target = new AuditTarget(nameof(User), user.Id),
                    Details = new { reason = "wrong_code", attempts = user.EmailCodeAttempts },
                });
            await db.SaveChangesAsync(cancellationToken);
            return user.EmailCodeAttempts >= EmailCodes.MaxAttempts
                ? AccessErrors.EmailCodeUsedUp.ToValidationResult()
                : WrongCode.ToValidationResult();
        }

        user.MarkEmailVerified(now);
        auditLog.Record(
            AuditEvents.Auth.EmailVerified,
            new AuditOptions { Target = new AuditTarget(nameof(User), user.Id), Details = new { email = user.Email } });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(CurrentUserResponse.From(user, await permissions.GetPermissionsAsync(user.Id, cancellationToken)));
    }

    private static Task RecordFailureAsync(IAuditLog auditLog, User user, string reason, CancellationToken cancellationToken) =>
        auditLog.RecordNowAsync(
            AuditEvents.Auth.EmailVerified,
            new AuditOptions
            {
                Outcome = AuditOutcome.Failed,
                Target = new AuditTarget(nameof(User), user.Id),
                Details = new { reason },
            },
            cancellationToken);
}

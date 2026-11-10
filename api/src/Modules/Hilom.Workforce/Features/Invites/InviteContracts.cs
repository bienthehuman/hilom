using FluentValidation;
using Hilom.Access.Accounts;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Features.Invites;

/// <param name="SentBy">Display name of whoever sent it, if they still have an account.</param>
public sealed record InviteResponse(
    Guid Id,
    string Email,
    InviteState State,
    DateTimeOffset SentAt,
    string? SentBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    IReadOnlyList<RoleName> Roles);

/// <param name="RoleIds">Roles the account gets when the invite is accepted.</param>
public sealed record SendInviteRequest(string Email, IReadOnlyList<Guid>? RoleIds);

public sealed record InviteTokenRequest(string Token);

/// <summary>What the accept page shows before the person sets a password.</summary>
public sealed record InviteLookupResponse(string FirstName, string LastName, string Email, DateTimeOffset ExpiresAt);

public sealed record AcceptInviteRequest(string Token, string Password);

/// <param name="Email">The new sign-in email, to pre-fill the sign-in page.</param>
public sealed record AcceptInviteResponse(string Email);

internal sealed class SendInviteRequestValidator : AbstractValidator<SendInviteRequest>
{
    public SendInviteRequestValidator() =>
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Enter the email to send the invite to.")
            .EmailAddress().WithMessage("Enter an email address like name@example.com.")
            .MaximumLength(Employee.EmailMaxLength);
}

internal sealed class InviteTokenRequestValidator : AbstractValidator<InviteTokenRequest>
{
    public InviteTokenRequestValidator() => RuleFor(r => r.Token).NotEmpty().MaximumLength(100);
}

internal sealed class AcceptInviteRequestValidator : AbstractValidator<AcceptInviteRequest>
{
    public AcceptInviteRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(12).WithMessage("Use at least 12 characters.")
            .MaximumLength(128);
    }
}

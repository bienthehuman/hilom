using FluentValidation;
using Hilom.Access.Domain;

namespace Hilom.Access.Features.Users;

public sealed record RoleRef(Guid Id, string Name, bool IsSystem, bool IsPortal);

/// <param name="IsLockedOut">Too many wrong passwords; sign-in is blocked until <c>LockoutEndsAt</c> or an unlock.</param>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    AccountType AccountType,
    UserStatus Status,
    bool IsLockedOut,
    DateTimeOffset? LockoutEndsAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoleRef> Roles);

public enum UserStatusFilter
{
    Active,
    Disabled,
    Locked,
}

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, IReadOnlyList<Guid>? RoleIds);

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Enter an email.")
            .EmailAddress().WithMessage("Enter an email address like name@example.com.")
            .MaximumLength(320);
        RuleFor(r => r.DisplayName).NotEmpty().WithMessage("Enter the person's name.").MaximumLength(200);
        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Set a temporary password.")
            .MinimumLength(12).WithMessage("Use at least 12 characters.")
            .MaximumLength(128);
    }
}

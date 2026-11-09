using FluentValidation;
using Hilom.Access.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Access.Features.Roles;

public sealed record PermissionResponse(string Key, string Description, IReadOnlyList<PermissionScope> AllowedScopes);

public sealed record PermissionGroupResponse(string Group, IReadOnlyList<PermissionResponse> Permissions);

public sealed record RoleSummaryResponse(Guid Id, string Name, string Description, bool IsSystem, int UserCount);

public sealed record GrantDto(string Permission, PermissionScope Scope);

/// <param name="Version">Send back when saving; a mismatch means someone else changed the role meanwhile.</param>
public sealed record RoleResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsSystem,
    uint Version,
    IReadOnlyList<GrantDto> Grants)
{
    internal static RoleResponse From(Role role) =>
        new(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            role.Version,
            [.. role.EffectiveGrants().Select(g => new GrantDto(g.Permission, g.Scope))]);
}

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<GrantDto> Grants);

public sealed record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<GrantDto> Grants, uint Version);

public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

internal sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Give the role a name.").MaximumLength(80);
        RuleFor(r => r.Description).MaximumLength(300);
        RuleFor(r => r.Grants).NotNull();
    }
}

internal sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Give the role a name.").MaximumLength(80);
        RuleFor(r => r.Description).MaximumLength(300);
        RuleFor(r => r.Grants).NotNull();
    }
}

internal sealed class SetUserRolesRequestValidator : AbstractValidator<SetUserRolesRequest>
{
    public SetUserRolesRequestValidator() => RuleFor(r => r.RoleIds).NotNull();
}

using Hilom.SharedKernel.Results;

namespace Hilom.Access.Domain;

/// <summary>Expected failures in the Access module, with stable codes the web app relies on.</summary>
public static class AccessErrors
{
    public static readonly Error SystemRoleProtected = Error.Conflict(
        "system_role_protected",
        "The System Administrator role can't be edited or deleted.");

    public static readonly Error RoleNotFound = Error.NotFound("role_not_found", "This role doesn't exist.");

    public static readonly Error RoleNameTaken = Error.Conflict("role_name_taken", "Another role already has this name.");

    public static readonly Error RoleInUse = Error.Conflict(
        "role_in_use",
        "Users still hold this role. Remove it from them before deleting it.");

    public static readonly Error UserNotFound = Error.NotFound("user_not_found", "This user doesn't exist.");

    public static readonly Error CannotChangeOwnRoles = Error.Forbidden(
        "cannot_change_own_roles",
        "You can't change your own roles. Ask another administrator.");

    public static readonly Error LastSystemAdministrator = Error.Conflict(
        "last_system_administrator",
        "This is the last active System Administrator. Give the role to someone else first.");

    public static readonly Error SystemAdministratorChangeNeedsRolesManage = Error.Forbidden(
        "system_administrator_requires_roles_manage",
        "Only someone who can manage roles can grant, remove, disable or enable a System Administrator.");

    public static readonly Error CannotDisableSelf = Error.Forbidden(
        "cannot_disable_self",
        "You can't disable your own account.");

    public static readonly Error EmailTaken = Error.Conflict("email_taken", "Another user already has this email.");

    public static readonly Error UnknownRoles = Error.Validation(
        "unknown_roles",
        "One or more of the chosen roles doesn't exist.");
}

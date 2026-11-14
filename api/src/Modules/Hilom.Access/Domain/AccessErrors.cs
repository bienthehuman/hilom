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

    public static readonly Error PortalRoleProtected = Error.Conflict(
        "portal_role_protected",
        "New patient portal accounts get this role, so it can't be deleted.");

    public static readonly Error PortalRoleIsForPortalAccounts = Error.Conflict(
        "portal_role_for_portal_accounts",
        "The patient portal role is only for patient portal accounts. Staff who also need the portal register a separate portal account.");

    public static readonly Error PortalAccountsHoldOnlyThePortalRole = Error.Conflict(
        "portal_account_roles",
        "Patient portal accounts can only hold the patient portal role, so they never get staff permissions.");

    public static readonly Error SelfRegistrationUnavailable = Error.BusinessRule(
        "registration_unavailable",
        "Creating portal accounts isn't available right now. Please contact the hospital.");

    public static readonly Error PortalEmailTaken = Error.Conflict(
        "email_taken",
        "An account with this email already exists. Sign in instead, or use another email.");

    public static readonly Error UnknownRoles = Error.Validation(
        "unknown_roles",
        "One or more of the chosen roles doesn't exist.");

    public static readonly Error EmailAlreadyVerified = Error.Conflict(
        "email_already_verified",
        "Your email is already confirmed.");

    /// <summary>Code only: the response carries a wait time, so it's built where the wait is known.</summary>
    public const string EmailCodeCooldown = "email_code_cooldown";

    public static readonly Error EmailCodeExpired = Error.BusinessRule(
        "email_code_expired",
        "This code has expired. Send a new one.");

    public static readonly Error EmailCodeUsedUp = Error.BusinessRule(
        "email_code_used_up",
        "That code can't be used any more. Send a new one.");

    /// <summary>Answered with 503, as the invite email does (WorkforceErrors.EmailUnavailable).</summary>
    public static readonly Error EmailUnavailable = Error.BusinessRule(
        "email_not_sent",
        "We couldn't send the email just now. Try again in a few minutes.");
}

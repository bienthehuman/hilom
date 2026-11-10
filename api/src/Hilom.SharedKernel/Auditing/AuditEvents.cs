namespace Hilom.SharedKernel.Auditing;

/// <summary>
/// The audit event catalog. Mirrors docs/AUDIT.md (a test fails if they drift apart). Modules add their events
/// here as they're built; the doc lists events for modules not built yet.
/// </summary>
public static class AuditEvents
{
    /// <summary>Category and key for automatic field-level change entries.</summary>
    public static readonly AuditEvent DataChanged = new("data.changed", "data", AuditSeverity.Info, "Record changed");

    public static class Auth
    {
        public static readonly AuditEvent LoginSucceeded = new("auth.login.succeeded", "auth", AuditSeverity.Info, "Signed in");
        public static readonly AuditEvent LoginFailed = new("auth.login.failed", "auth", AuditSeverity.Critical, "Sign-in failed");
        public static readonly AuditEvent AccountLockedOut = new("auth.account.locked_out", "auth", AuditSeverity.Critical, "Account locked after failed sign-ins");
        public static readonly AuditEvent Logout = new("auth.logout", "auth", AuditSeverity.Info, "Signed out");
    }

    public static class Access
    {
        public static readonly AuditEvent UserCreated = new("access.user.created", "access", AuditSeverity.Notice, "User created");
        public static readonly AuditEvent UserDisabled = new("access.user.disabled", "access", AuditSeverity.Critical, "User disabled");
        public static readonly AuditEvent UserEnabled = new("access.user.enabled", "access", AuditSeverity.Critical, "User enabled");
        public static readonly AuditEvent UserUnlocked = new("access.user.unlocked", "access", AuditSeverity.Notice, "User unlocked");
        public static readonly AuditEvent UserRolesChanged = new("access.user.roles_changed", "access", AuditSeverity.Critical, "User's roles changed");
        public static readonly AuditEvent RoleCreated = new("access.role.created", "access", AuditSeverity.Critical, "Role created");
        public static readonly AuditEvent RoleRenamed = new("access.role.renamed", "access", AuditSeverity.Notice, "Role renamed or redescribed");
        public static readonly AuditEvent RolePermissionsChanged = new("access.role.permissions_changed", "access", AuditSeverity.Critical, "Role's permissions changed");
        public static readonly AuditEvent RoleDeleted = new("access.role.deleted", "access", AuditSeverity.Critical, "Role deleted");
        public static readonly AuditEvent Denied = new("access.denied", "access", AuditSeverity.Critical, "Access denied");
        public static readonly AuditEvent AuditViewed = new("access.audit.viewed", "access", AuditSeverity.Info, "Audit log viewed");
    }

    public static IReadOnlyList<AuditEvent> All { get; } =
    [
        DataChanged,
        Auth.LoginSucceeded,
        Auth.LoginFailed,
        Auth.AccountLockedOut,
        Auth.Logout,
        Access.UserCreated,
        Access.UserDisabled,
        Access.UserEnabled,
        Access.UserUnlocked,
        Access.UserRolesChanged,
        Access.RoleCreated,
        Access.RoleRenamed,
        Access.RolePermissionsChanged,
        Access.RoleDeleted,
        Access.Denied,
        Access.AuditViewed,
    ];
}

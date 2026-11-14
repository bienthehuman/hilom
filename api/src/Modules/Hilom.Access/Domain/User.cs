using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;

namespace Hilom.Access.Domain;

public enum UserStatus
{
    Active,
    Disabled,
}

/// <summary>Staff accounts are made by administrators or invites; portal accounts by patients and families themselves.</summary>
public enum AccountType
{
    Staff,
    Portal,
}

/// <summary>
/// Someone who can sign in. Holds no profile data: staff details live on Workforce employees,
/// patients reach records through patient account links.
/// </summary>
public sealed class User : Entity
{
    private User()
    {
    }

    public User(string email, string displayName, AccountType accountType = AccountType.Staff)
    {
        Email = email.Trim();
        DisplayName = displayName.Trim();
        AccountType = accountType;
        Status = UserStatus.Active;
        SecurityStamp = Guid.NewGuid();
    }

    /// <summary>Stored as citext, so lookups ignore case.</summary>
    public string Email { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    /// <summary>Never changes. Portal accounts only ever hold the portal role (docs/RBAC.md, "Guardrails").</summary>
    public AccountType AccountType { get; private set; }

    [SensitiveData]
    public string PasswordHash { get; private set; } = "";

    /// <summary>Changes whenever existing sessions must end (password change, disabling). Checked on every request.</summary>
    [SensitiveData]
    public Guid SecurityStamp { get; private set; }

    public UserStatus Status { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    /// <summary>Bumped when the user's roles change, so cached permissions reload (slice 0.7).</summary>
    public int PermissionsVersion { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>
    /// The password was set by someone else (an administrator), so the user must choose their own before doing
    /// anything else. Until then the API answers 403 <c>password_change_required</c> almost everywhere.
    /// </summary>
    public bool MustChangePassword { get; private set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt > now;

    /// <summary>Sets a password the user chose themselves, and signs them out everywhere.</summary>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
        MustChangePassword = false;
    }

    /// <summary>
    /// Sets a password someone else chose (an administrator creating the account), and signs the user out
    /// everywhere. They must replace it on their next sign-in.
    /// </summary>
    public void SetTemporaryPassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
        MustChangePassword = true;
    }

    /// <summary>Upgrades the stored hash after a successful login (e.g. more hashing rounds). Keeps sessions.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void Disable()
    {
        Status = UserStatus.Disabled;
        SecurityStamp = Guid.NewGuid();
    }

    public void Enable() => Status = UserStatus.Active;

    /// <summary>Clears a lockout early, e.g. after the user confirmed their identity with an administrator.</summary>
    public void Unlock()
    {
        LockoutEndsAt = null;
        FailedLoginCount = 0;
    }

    /// <summary>Call when this user's roles change, so their cached permissions reload on the next request.</summary>
    public void PermissionsChanged() => PermissionsVersion++;
}

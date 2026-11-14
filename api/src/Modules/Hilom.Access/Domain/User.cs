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

    /// <summary>
    /// When the user proved they read this email address. Only self-registered portal accounts start without it;
    /// until then they can sign in but can't ask to be linked to a patient.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    /// <summary>Hash of the latest email verification code; null once used or never sent.</summary>
    [SensitiveData]
    public string? EmailCodeHash { get; private set; }

    public DateTimeOffset? EmailCodeSentAt { get; private set; }

    public DateTimeOffset? EmailCodeExpiresAt { get; private set; }

    /// <summary>Wrong codes entered against the latest code. At the limit the code stops working.</summary>
    public int EmailCodeAttempts { get; private set; }

    /// <summary>A self-registered portal account that hasn't confirmed its email yet.</summary>
    public bool MustVerifyEmail => AccountType == AccountType.Portal && EmailVerifiedAt is null;

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

    /// <summary>Accounts whose email is known to work (made by staff, from an invite, or seeded).</summary>
    public void MarkEmailVerified(DateTimeOffset at)
    {
        EmailVerifiedAt ??= at;
        ClearEmailCode();
    }

    /// <summary>Self-registration: the account must confirm its email with a code before requesting links.</summary>
    public void RequireEmailVerification()
    {
        EmailVerifiedAt = null;
        ClearEmailCode();
    }

    /// <summary>Stores a newly sent code, replacing any earlier one and its wrong attempts.</summary>
    public void IssueEmailCode(string codeHash, DateTimeOffset sentAt, DateTimeOffset expiresAt)
    {
        EmailCodeHash = codeHash;
        EmailCodeSentAt = sentAt;
        EmailCodeExpiresAt = expiresAt;
        EmailCodeAttempts = 0;
    }

    /// <summary>Counts a wrong code.</summary>
    public void EmailCodeFailed() => EmailCodeAttempts++;

    private void ClearEmailCode()
    {
        EmailCodeHash = null;
        EmailCodeExpiresAt = null;
        EmailCodeAttempts = 0;
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

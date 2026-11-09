using Hilom.SharedKernel.Domain;

namespace Hilom.Access.Domain;

public enum UserStatus
{
    Active,
    Disabled,
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

    public User(string email, string displayName)
    {
        Email = email.Trim();
        DisplayName = displayName.Trim();
        Status = UserStatus.Active;
        SecurityStamp = Guid.NewGuid();
    }

    /// <summary>Stored as citext, so lookups ignore case.</summary>
    public string Email { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    public string PasswordHash { get; private set; } = "";

    /// <summary>Changes whenever existing sessions must end (password change, disabling). Checked on every request.</summary>
    public Guid SecurityStamp { get; private set; }

    public UserStatus Status { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    /// <summary>Bumped when the user's roles change, so cached permissions reload (slice 0.7).</summary>
    public int PermissionsVersion { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt > now;

    /// <summary>Sets a new password and signs the user out everywhere.</summary>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
    }

    /// <summary>Upgrades the stored hash after a successful login (e.g. more hashing rounds). Keeps sessions.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void RecordFailedLogin(DateTimeOffset now, LockoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        FailedLoginCount++;
        if (FailedLoginCount >= policy.MaxFailedAttempts)
        {
            LockoutEndsAt = now + policy.Duration;
            FailedLoginCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void Disable()
    {
        Status = UserStatus.Disabled;
        SecurityStamp = Guid.NewGuid();
    }

    public void Enable() => Status = UserStatus.Active;

    /// <summary>Call when this user's roles change, so their cached permissions reload on the next request.</summary>
    public void PermissionsChanged() => PermissionsVersion++;
}

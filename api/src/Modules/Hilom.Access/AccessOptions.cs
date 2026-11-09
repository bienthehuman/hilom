using System.ComponentModel.DataAnnotations;
using Hilom.Access.Domain;

namespace Hilom.Access;

/// <summary>Sign-in and session settings (configuration section <c>Access</c>).</summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    public LockoutPolicy Lockout { get; set; } = new();

    /// <summary>A session ends after this long without activity. One shift by default.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Sign-in attempts allowed per minute from one IP address, across all accounts.</summary>
    [Range(1, int.MaxValue)]
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>First System Administrator, created only when no users exist yet.</summary>
    public SeedAdminOptions? SeedAdmin { get; set; }
}

public sealed class SeedAdminOptions
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(12)]
    public string Password { get; set; } = "";

    public string DisplayName { get; set; } = "System Administrator";
}

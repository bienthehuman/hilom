namespace Hilom.Access.Domain;

/// <summary>After this many wrong passwords in a row, sign-in is blocked for <see cref="Duration"/>.</summary>
public sealed class LockoutPolicy
{
    public int MaxFailedAttempts { get; set; } = 5;

    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(15);
}

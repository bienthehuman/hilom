using System.ComponentModel.DataAnnotations;

namespace Hilom.Workforce;

public sealed class InviteOptions
{
    public const string SectionName = "Workforce:Invites";

    /// <summary>How long an invite link works.</summary>
    [Range(typeof(TimeSpan), "00:10:00", "30.00:00:00")]
    public TimeSpan ValidFor { get; set; } = TimeSpan.FromDays(7);
}

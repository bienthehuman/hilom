using System.ComponentModel.DataAnnotations;

namespace Hilom.Infrastructure.Configuration;

/// <summary>Hospital-wide settings, validated at startup.</summary>
public sealed class HospitalOptions
{
    public const string SectionName = "Hospital";

    [Required]
    public string Name { get; set; } = "Hilom General Hospital";

    /// <summary>IANA time zone used to display times and define shift days. Data is always stored in UTC.</summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Manila";
}

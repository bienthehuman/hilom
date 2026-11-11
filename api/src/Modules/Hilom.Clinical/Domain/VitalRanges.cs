namespace Hilom.Clinical.Domain;

/// <summary>Where a reading sits against its range. Shown as an arrow and color (docs/DESIGN_SYSTEM.md §3).</summary>
public enum VitalFlag
{
    Normal,
    Low,
    High,
    CriticalLow,
    CriticalHigh,
}

/// <summary>
/// One measure's adult range. Normal is inclusive at both ends. A reading at or beyond a critical limit is
/// critical; outside normal but not critical is low or high.
/// </summary>
public sealed class VitalRange
{
    public decimal NormalLow { get; set; }

    public decimal NormalHigh { get; set; }

    /// <summary>At or below this is critically low. None for measures without one.</summary>
    public decimal? CriticalLow { get; set; }

    /// <summary>At or above this is critically high. None for measures without one (e.g. oxygen saturation).</summary>
    public decimal? CriticalHigh { get; set; }

    public VitalFlag Flag(decimal value) => value switch
    {
        _ when CriticalLow is { } low && value <= low => VitalFlag.CriticalLow,
        _ when CriticalHigh is { } high && value >= high => VitalFlag.CriticalHigh,
        _ when value < NormalLow => VitalFlag.Low,
        _ when value > NormalHigh => VitalFlag.High,
        _ => VitalFlag.Normal,
    };

    /// <summary>Why this range doesn't make sense, or null if it does.</summary>
    public string? Problem() =>
        NormalLow > NormalHigh ? "the normal low is above the normal high"
        : CriticalLow is { } low && low >= NormalLow ? "the critical low isn't below the normal range"
        : CriticalHigh is { } high && high <= NormalHigh ? "the critical high isn't above the normal range"
        : null;
}

/// <summary>
/// Normal and critical ranges for vital signs: one backend setting (<c>Clinical:VitalRanges</c>) that the API's
/// flags, the forms and the trend chart all use (docs/DESIGN_SYSTEM.md §3). The defaults are common adult
/// ranges for the demo, not clinical guidance; a hospital sets its own.
/// </summary>
public sealed class VitalRangeOptions
{
    public const string SectionName = "Clinical:VitalRanges";

    public VitalRange TemperatureC { get; set; } = new() { NormalLow = 36.0m, NormalHigh = 37.9m, CriticalLow = 35.0m, CriticalHigh = 40.0m };

    public VitalRange SystolicBp { get; set; } = new() { NormalLow = 90, NormalHigh = 139, CriticalLow = 79, CriticalHigh = 180 };

    public VitalRange DiastolicBp { get; set; } = new() { NormalLow = 60, NormalHigh = 89, CriticalLow = 39, CriticalHigh = 120 };

    public VitalRange HeartRate { get; set; } = new() { NormalLow = 60, NormalHigh = 100, CriticalLow = 40, CriticalHigh = 130 };

    public VitalRange RespiratoryRate { get; set; } = new() { NormalLow = 12, NormalHigh = 20, CriticalLow = 8, CriticalHigh = 30 };

    public VitalRange OxygenSaturation { get; set; } = new() { NormalLow = 95, NormalHigh = 100, CriticalLow = 89 };

    public IEnumerable<(string Name, VitalRange Range)> All() =>
    [
        (nameof(TemperatureC), TemperatureC),
        (nameof(SystolicBp), SystolicBp),
        (nameof(DiastolicBp), DiastolicBp),
        (nameof(HeartRate), HeartRate),
        (nameof(RespiratoryRate), RespiratoryRate),
        (nameof(OxygenSaturation), OxygenSaturation),
    ];
}

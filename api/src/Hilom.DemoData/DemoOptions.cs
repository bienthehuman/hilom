using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Hilom.DemoData;

/// <summary>How much history to generate. <see cref="DemoSizes"/> says what each one means.</summary>
public enum DemoScale
{
    /// <summary>Tens of patients and a few weeks. For tests.</summary>
    Tiny,

    /// <summary>Hundreds of patients and two months. Quick to seed on a laptop.</summary>
    Small,

    /// <summary>Thousands of patients and about four months: hundreds of thousands of audit entries and tens of thousands of vitals, enough for the Phase 4 slow-query check.</summary>
    Standard,
}

/// <summary>Demo mode settings (configuration section <c>Demo</c>). Off unless <see cref="Enabled"/>.</summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Seeds fictional data on an empty database, shows demo logins on the sign-in pages and resets nightly.</summary>
    public bool Enabled { get; set; }

    /// <summary>The same seed (with the same scale and <see cref="Now"/>) generates the same data.</summary>
    public int Seed { get; set; } = 20261113;

    public DemoScale Scale { get; set; } = DemoScale.Standard;

    /// <summary>
    /// The moment the generated history ends. Left empty it's the current hour, so the demo always looks
    /// current; set it to reproduce an earlier dataset exactly.
    /// </summary>
    public DateTimeOffset? Now { get; set; }

    /// <summary>The password every demo login uses. It's shown on the sign-in page, so it protects nothing real.</summary>
    [MinLength(12)]
    public string Password { get; set; } = "Hilom-Demo-2026";

    /// <summary>Wipe and re-seed every night, so whatever visitors did is gone by morning.</summary>
    public bool NightlyReset { get; set; } = true;

    /// <summary>Hospital time of day for the reset, 24-hour <c>HH:mm</c>.</summary>
    public string ResetAt { get; set; } = "03:00";

    public static bool TryParseResetAt(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

/// <summary>What a <see cref="DemoScale"/> generates.</summary>
public sealed record DemoSize(
    int HistoryDays,
    int Patients,
    int Physicians,
    int Wards,
    int BedsPerWard,
    int NursesPerWard,
    int ClinicSlotsPerPhysicianDay,
    double ClinicFill,
    double AdmissionsPerWardDay)
{
    public const int FutureDays = 14;
}

public static class DemoSizes
{
    public static DemoSize For(DemoScale scale) => scale switch
    {
        DemoScale.Tiny => new DemoSize(HistoryDays: 21, Patients: 60, Physicians: 4, Wards: 3, BedsPerWard: 6, NursesPerWard: 4, ClinicSlotsPerPhysicianDay: 4, ClinicFill: 0.6, AdmissionsPerWardDay: 0.9),
        DemoScale.Small => new DemoSize(HistoryDays: 60, Patients: 800, Physicians: 8, Wards: 6, BedsPerWard: 12, NursesPerWard: 6, ClinicSlotsPerPhysicianDay: 8, ClinicFill: 0.6, AdmissionsPerWardDay: 1.4),
        _ => new DemoSize(HistoryDays: 110, Patients: 3500, Physicians: 12, Wards: 9, BedsPerWard: 16, NursesPerWard: 8, ClinicSlotsPerPhysicianDay: 10, ClinicFill: 0.65, AdmissionsPerWardDay: 1.7),
    };
}

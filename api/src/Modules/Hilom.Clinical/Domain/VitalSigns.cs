using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;

namespace Hilom.Clinical.Domain;

/// <summary>The measurements taken at one time. Any subset can be taken, but at least one.</summary>
public sealed record VitalMeasurements(
    decimal? TemperatureC,
    short? SystolicBp,
    short? DiastolicBp,
    short? HeartRate,
    short? RespiratoryRate,
    short? OxygenSaturation,
    short? PainScore,
    decimal? WeightKg,
    decimal? HeightCm)
{
    /// <summary>Names of the measurements taken, for the audit log.</summary>
    public IReadOnlyList<string> Taken() =>
    [
        .. new (string Name, bool Taken)[]
        {
            ("temperatureC", TemperatureC is not null),
            ("bloodPressure", SystolicBp is not null),
            ("heartRate", HeartRate is not null),
            ("respiratoryRate", RespiratoryRate is not null),
            ("oxygenSaturation", OxygenSaturation is not null),
            ("painScore", PainScore is not null),
            ("weightKg", WeightKg is not null),
            ("heightCm", HeightCm is not null),
        }.Where(m => m.Taken).Select(m => m.Name),
    ];
}

/// <summary>
/// One set of vital signs on an encounter (docs/DATA_MODEL.md, vital_signs): the same model for clinic visits and
/// ward stays. Blood pressure is two numbers, not a string. BMI is calculated, not stored. Who took them is an
/// employee, not free text.
/// </summary>
public sealed class VitalSigns : Entity, IPatientRecord
{
    public const int NotesMaxLength = 500;

    // Plausible limits, also check constraints in the database. Outside them is a typo, not a patient.
    public const decimal TemperatureMin = 30m;
    public const decimal TemperatureMax = 45m;
    public const short SystolicMin = 40;
    public const short SystolicMax = 300;
    public const short DiastolicMin = 20;
    public const short DiastolicMax = 200;
    public const short HeartRateMin = 20;
    public const short HeartRateMax = 300;
    public const short RespiratoryRateMin = 2;
    public const short RespiratoryRateMax = 80;
    public const short PainMax = 10;
    public const decimal WeightMin = 0.5m;
    public const decimal WeightMax = 500m;
    public const decimal HeightMin = 20m;
    public const decimal HeightMax = 300m;

    private VitalSigns()
    {
    }

    public VitalSigns(Guid encounterId, Guid patientId, Guid recordedBy, DateTimeOffset recordedAt, VitalMeasurements m, string? notes)
    {
        EncounterId = encounterId;
        PatientId = patientId;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
        TemperatureC = m.TemperatureC;
        SystolicBp = m.SystolicBp;
        DiastolicBp = m.DiastolicBp;
        HeartRate = m.HeartRate;
        RespiratoryRate = m.RespiratoryRate;
        OxygenSaturation = m.OxygenSaturation;
        PainScore = m.PainScore;
        WeightKg = m.WeightKg;
        HeightCm = m.HeightCm;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public Guid EncounterId { get; private set; }

    /// <summary>Copied from the encounter, so audit entries and the patient's history find it directly.</summary>
    public Guid PatientId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>The employee who took them.</summary>
    public Guid RecordedBy { get; private set; }

    public decimal? TemperatureC { get; private set; }

    public short? SystolicBp { get; private set; }

    public short? DiastolicBp { get; private set; }

    public short? HeartRate { get; private set; }

    public short? RespiratoryRate { get; private set; }

    /// <summary>SpO₂, percent.</summary>
    public short? OxygenSaturation { get; private set; }

    /// <summary>0 (none) to 10 (worst).</summary>
    public short? PainScore { get; private set; }

    public decimal? WeightKg { get; private set; }

    public decimal? HeightCm { get; private set; }

    /// <summary>Free text. Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string? Notes { get; private set; }

    /// <summary>Body mass index from this set's weight and height, one decimal; null without both.</summary>
    public decimal? Bmi => WeightKg is { } kg && HeightCm is { } cm && cm > 0
        ? Math.Round(kg / (cm / 100m * (cm / 100m)), 1)
        : null;
}

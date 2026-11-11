using FluentValidation;
using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Voiding;

namespace Hilom.Clinical.Features.Vitals;

/// <summary>How each measured value compares with its range; null for measures not taken or without a range.</summary>
public sealed record VitalFlagsResponse(
    VitalFlag? TemperatureC,
    VitalFlag? SystolicBp,
    VitalFlag? DiastolicBp,
    VitalFlag? HeartRate,
    VitalFlag? RespiratoryRate,
    VitalFlag? OxygenSaturation);

/// <param name="RecordedBy">The employee's name.</param>
/// <param name="Bmi">From this set's weight and height, when both were taken.</param>
/// <param name="Voided">Set when it was voided as entered in error; it stays in the list, left out of trends.</param>
/// <param name="CanVoid">Whether the caller may void it (<c>clinical.records.void</c>: all, or own = they recorded it).</param>
public sealed record VitalSignsResponse(
    Guid Id,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    decimal? TemperatureC,
    short? SystolicBp,
    short? DiastolicBp,
    short? HeartRate,
    short? RespiratoryRate,
    short? OxygenSaturation,
    short? PainScore,
    decimal? WeightKg,
    decimal? HeightCm,
    decimal? Bmi,
    string? Notes,
    VitalFlagsResponse Flags,
    VoidedResponse? Voided,
    bool CanVoid);

public sealed record VitalRangeResponse(decimal NormalLow, decimal NormalHigh, decimal? CriticalLow, decimal? CriticalHigh);

/// <summary>The ranges the API flags against, so forms and charts show the same thing.</summary>
public sealed record VitalRangesResponse(
    VitalRangeResponse TemperatureC,
    VitalRangeResponse SystolicBp,
    VitalRangeResponse DiastolicBp,
    VitalRangeResponse HeartRate,
    VitalRangeResponse RespiratoryRate,
    VitalRangeResponse OxygenSaturation);

/// <param name="RecordedAt">When they were taken, for charting after the fact; now when left out.</param>
public sealed record RecordVitalSignsRequest(
    DateTimeOffset? RecordedAt,
    decimal? TemperatureC,
    short? SystolicBp,
    short? DiastolicBp,
    short? HeartRate,
    short? RespiratoryRate,
    short? OxygenSaturation,
    short? PainScore,
    decimal? WeightKg,
    decimal? HeightCm,
    string? Notes)
{
    public VitalMeasurements ToMeasurements() =>
        new(TemperatureC, SystolicBp, DiastolicBp, HeartRate, RespiratoryRate, OxygenSaturation, PainScore, WeightKg, HeightCm);
}

internal sealed class RecordVitalSignsRequestValidator : AbstractValidator<RecordVitalSignsRequest>
{
    public RecordVitalSignsRequestValidator()
    {
        RuleFor(r => r)
            .Must(r => r.ToMeasurements().Taken().Count > 0)
            .WithName("measurements")
            .OverridePropertyName("measurements")
            .WithMessage("Enter at least one measurement.");

        RuleFor(r => r.TemperatureC)
            .InclusiveBetween(VitalSigns.TemperatureMin, VitalSigns.TemperatureMax)
            .WithMessage($"Enter a temperature from {VitalSigns.TemperatureMin} to {VitalSigns.TemperatureMax} °C.")
            .PrecisionScale(4, 1, ignoreTrailingZeros: true)
            .WithMessage("Use at most one decimal place.");
        RuleFor(r => r.SystolicBp)
            .InclusiveBetween(VitalSigns.SystolicMin, VitalSigns.SystolicMax)
            .WithMessage($"Enter a systolic pressure from {VitalSigns.SystolicMin} to {VitalSigns.SystolicMax}.");
        RuleFor(r => r.DiastolicBp)
            .InclusiveBetween(VitalSigns.DiastolicMin, VitalSigns.DiastolicMax)
            .WithMessage($"Enter a diastolic pressure from {VitalSigns.DiastolicMin} to {VitalSigns.DiastolicMax}.");
        RuleFor(r => r.DiastolicBp)
            .NotNull().When(r => r.SystolicBp is not null).WithMessage("Enter both blood pressure numbers.");
        RuleFor(r => r.SystolicBp)
            .NotNull().When(r => r.DiastolicBp is not null).WithMessage("Enter both blood pressure numbers.");
        RuleFor(r => r.DiastolicBp)
            .LessThan(r => r.SystolicBp).When(r => r.SystolicBp is not null && r.DiastolicBp is not null)
            .WithMessage("Diastolic must be lower than systolic.");
        RuleFor(r => r.HeartRate)
            .InclusiveBetween(VitalSigns.HeartRateMin, VitalSigns.HeartRateMax)
            .WithMessage($"Enter a heart rate from {VitalSigns.HeartRateMin} to {VitalSigns.HeartRateMax}.");
        RuleFor(r => r.RespiratoryRate)
            .InclusiveBetween(VitalSigns.RespiratoryRateMin, VitalSigns.RespiratoryRateMax)
            .WithMessage($"Enter a respiratory rate from {VitalSigns.RespiratoryRateMin} to {VitalSigns.RespiratoryRateMax}.");
        RuleFor(r => r.OxygenSaturation)
            .InclusiveBetween((short)0, (short)100).WithMessage("Enter oxygen saturation from 0 to 100%.");
        RuleFor(r => r.PainScore)
            .InclusiveBetween((short)0, VitalSigns.PainMax).WithMessage("Enter a pain score from 0 to 10.");
        RuleFor(r => r.WeightKg)
            .InclusiveBetween(VitalSigns.WeightMin, VitalSigns.WeightMax)
            .WithMessage($"Enter a weight from {VitalSigns.WeightMin} to {VitalSigns.WeightMax} kg.")
            .PrecisionScale(5, 2, ignoreTrailingZeros: true).WithMessage("Use at most two decimal places.");
        RuleFor(r => r.HeightCm)
            .InclusiveBetween(VitalSigns.HeightMin, VitalSigns.HeightMax)
            .WithMessage($"Enter a height from {VitalSigns.HeightMin} to {VitalSigns.HeightMax} cm.")
            .PrecisionScale(5, 2, ignoreTrailingZeros: true).WithMessage("Use at most two decimal places.");
        RuleFor(r => r.Notes).MaximumLength(VitalSigns.NotesMaxLength);
    }
}

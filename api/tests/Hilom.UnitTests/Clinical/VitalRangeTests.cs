using Hilom.Clinical.Domain;

namespace Hilom.UnitTests.Clinical;

public sealed class VitalRangeTests
{
    private static readonly VitalRange HeartRate = new VitalRangeOptions().HeartRate;

    [Theory]
    [InlineData(39, VitalFlag.CriticalLow)]
    [InlineData(40, VitalFlag.CriticalLow)]
    [InlineData(41, VitalFlag.Low)]
    [InlineData(59, VitalFlag.Low)]
    [InlineData(60, VitalFlag.Normal)]
    [InlineData(100, VitalFlag.Normal)]
    [InlineData(101, VitalFlag.High)]
    [InlineData(129, VitalFlag.High)]
    [InlineData(130, VitalFlag.CriticalHigh)]
    public void Normal_is_inclusive_and_critical_starts_at_its_limit(int value, VitalFlag expected)
    {
        HeartRate.Flag(value).ShouldBe(expected);
    }

    [Fact]
    public void A_measure_without_a_critical_high_is_never_critically_high()
    {
        var spo2 = new VitalRangeOptions().OxygenSaturation;

        spo2.Flag(100).ShouldBe(VitalFlag.Normal);
        spo2.Flag(89).ShouldBe(VitalFlag.CriticalLow);
        spo2.Flag(92).ShouldBe(VitalFlag.Low);
    }

    [Fact]
    public void The_default_ranges_are_consistent()
    {
        new VitalRangeOptions().All().ShouldAllBe(r => r.Range.Problem() == null);
    }

    [Fact]
    public void Out_of_order_limits_are_reported()
    {
        new VitalRange { NormalLow = 60, NormalHigh = 100, CriticalLow = 70 }.Problem().ShouldNotBeNull();
        new VitalRange { NormalLow = 60, NormalHigh = 100, CriticalHigh = 90 }.Problem().ShouldNotBeNull();
        new VitalRange { NormalLow = 100, NormalHigh = 60 }.Problem().ShouldNotBeNull();
    }

    [Fact]
    public void Bmi_needs_both_weight_and_height()
    {
        VitalSigns With(decimal? kg, decimal? cm) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, new VitalMeasurements(null, null, null, 80, null, null, null, kg, cm), null);

        With(70m, 175m).Bmi.ShouldBe(22.9m);
        With(70m, null).Bmi.ShouldBeNull();
    }
}

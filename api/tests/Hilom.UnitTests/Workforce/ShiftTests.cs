using System.Globalization;
using Hilom.Workforce.Domain;

namespace Hilom.UnitTests.Workforce;

public sealed class ShiftTests
{
    [Theory]
    [InlineData("06:00", "14:00", false, 8)]
    [InlineData("22:00", "06:00", true, 8)]
    [InlineData("19:00", "07:00", true, 12)]
    [InlineData("23:30", "00:15", true, 0.75)]
    public void Duration_counts_across_midnight(string starts, string ends, bool crosses, double hours)
    {
        var shift = Shift.Create("X", "Test", TimeOnly.Parse(starts, CultureInfo.InvariantCulture), TimeOnly.Parse(ends, CultureInfo.InvariantCulture), ShiftColor.Am, 1).Value;

        shift.CrossesMidnight.ShouldBe(crosses);
        shift.Duration.ShouldBe(TimeSpan.FromHours(hours));
    }

    [Fact]
    public void A_shift_must_have_a_length()
    {
        var result = Shift.Create("X", "Test", new TimeOnly(7, 0), new TimeOnly(7, 0), ShiftColor.Am, 1);

        result.IsSuccess.ShouldBeFalse();
        result.Error.FieldErrors!["endsAt"].ShouldNotBeEmpty();
    }
}

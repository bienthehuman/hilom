using Hilom.Scheduling.Domain;

namespace Hilom.UnitTests.Scheduling;

public sealed class WeeklyPatternTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    // Monday 16 Nov 2026 to Sunday 22 Nov 2026.
    private static WeeklyPattern Pattern(params DayOfWeek[] days) =>
        new(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 22), days.ToHashSet(), new TimeOnly(9, 0), new TimeOnly(10, 30), 30);

    [Fact]
    public void Slots_fill_the_window_on_the_chosen_days_only()
    {
        var slots = Pattern(DayOfWeek.Monday, DayOfWeek.Thursday).Slots(Manila).ToList();

        slots.Count.ShouldBe(6);
        slots.Select(s => TimeZoneInfo.ConvertTime(s.StartsAt, Manila).DayOfWeek).Distinct()
            .ShouldBe([DayOfWeek.Monday, DayOfWeek.Thursday]);
    }

    [Fact]
    public void Times_are_hospital_time_stored_as_utc()
    {
        var first = Pattern(DayOfWeek.Monday).Slots(Manila).First();

        // 09:00 in Manila (UTC+8) is 01:00 UTC.
        first.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 16, 1, 0, 0, TimeSpan.Zero));
        first.EndsAt.ShouldBe(new DateTimeOffset(2026, 11, 16, 1, 30, 0, TimeSpan.Zero));
        first.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_window_that_does_not_divide_evenly_leaves_the_rest_unused()
    {
        var pattern = Pattern(DayOfWeek.Monday) with { SlotMinutes = 40 };

        var slots = pattern.Slots(Manila).ToList();

        // 09:00–09:40 and 09:40–10:20; 10:20–10:30 is too short.
        slots.Count.ShouldBe(2);
        slots[^1].EndsAt.ShouldBe(new DateTimeOffset(2026, 11, 16, 2, 20, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Slots_are_consecutive_and_in_time_order()
    {
        var slots = Pattern(DayOfWeek.Monday, DayOfWeek.Tuesday).Slots(Manila).ToList();

        for (var i = 1; i < slots.Count; i++)
        {
            slots[i].StartsAt.ShouldBeGreaterThanOrEqualTo(slots[i - 1].EndsAt);
        }
    }

    [Fact]
    public void A_slot_must_end_after_it_starts()
    {
        var start = new DateTimeOffset(2026, 11, 16, 1, 0, 0, TimeSpan.Zero);

        AvailabilitySlot.Create(Guid.NewGuid(), start, start).IsSuccess.ShouldBeFalse();
        AvailabilitySlot.Create(Guid.NewGuid(), start, start.AddMinutes(30)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_closed_slot_cannot_be_closed_again()
    {
        var slot = AvailabilitySlot.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30)).Value;

        slot.Close().IsSuccess.ShouldBeTrue();
        slot.Close().Error!.Code.ShouldBe("slot_not_open");
    }
}

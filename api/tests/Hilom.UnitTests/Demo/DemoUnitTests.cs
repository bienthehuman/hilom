using Hilom.DemoData;
using Hilom.SharedKernel.Domain;

namespace Hilom.UnitTests.Demo;

public sealed class DemoUnitTests
{
    [Fact]
    public void Entity_ids_come_from_the_override_while_it_is_in_force_and_from_uuid_v7_after()
    {
        var next = 0;
        Guid Make() => new(next++, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

        Guid first;
        using (EntityIds.Use(Make))
        {
            first = EntityIds.Next();
            EntityIds.Next().ShouldBe(new Guid(1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]));
        }

        first.ShouldBe(new Guid(0, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]));
        EntityIds.Next().Version.ShouldBe(7);
    }

    [Fact]
    public async Task An_id_override_only_reaches_work_started_while_it_is_in_force()
    {
        var gate = new TaskCompletionSource();
        var startedBefore = Task.Run(async () =>
        {
            await gate.Task;
            return EntityIds.Next();
        }, TestContext.Current.CancellationToken);

        using (EntityIds.Use(() => Guid.Empty))
        {
            var startedDuring = await Task.Run(EntityIds.Next, TestContext.Current.CancellationToken);
            gate.SetResult();

            startedDuring.ShouldBe(Guid.Empty);
            (await startedBefore).Version.ShouldBe(7);
        }
    }

    [Fact]
    public void The_same_seed_draws_the_same_numbers_and_ids()
    {
        var at = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var a = new DemoRandom(11);
        var b = new DemoRandom(11);
        var c = new DemoRandom(12);

        Enumerable.Range(0, 50).Select(_ => a.Next(0, 1000)).ShouldBe(Enumerable.Range(0, 50).Select(_ => b.Next(0, 1000)));
        new DemoRandom(11).NewId(at).ShouldBe(new DemoRandom(11).NewId(at));
        new DemoRandom(11).NewId(at).ShouldNotBe(c.NewId(at));
    }

    [Fact]
    public void Generated_ids_are_uuid_v7_and_sort_by_time()
    {
        var random = new DemoRandom(3);
        var early = random.NewId(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var late = random.NewId(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));

        early.Version.ShouldBe(7);
        late.Version.ShouldBe(7);
        string.CompareOrdinal(early.ToString(), late.ToString()).ShouldBeLessThan(0);
    }

    [Fact]
    public void Weighted_picks_follow_their_weights_and_never_choose_a_zero_weight()
    {
        var random = new DemoRandom(5);
        var picks = Enumerable.Range(0, 4000).Select(_ => random.Pick(("often", 9.0), ("rare", 1.0), ("never", 0.0))).ToList();

        picks.ShouldNotContain("never");
        picks.Count(p => p == "often").ShouldBeInRange(3400, 3800);
    }

    [Theory]
    [InlineData("2026-11-13T01:00:00Z", "2026-11-13T19:00:00Z")] // 09:00 in Manila: today's 03:00 is behind, tomorrow's is next
    [InlineData("2026-11-12T18:00:00Z", "2026-11-12T19:00:00Z")] // 02:00 in Manila: 03:00 is an hour away
    [InlineData("2026-11-12T19:00:00Z", "2026-11-13T19:00:00Z")] // exactly 03:00: strictly after
    public void The_next_reset_is_the_next_time_the_hospital_clock_reads_the_reset_time(string now, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        DemoResetService.NextResetAfter(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), new TimeOnly(3, 0), zone)
            .ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_reset_time_that_does_not_exist_on_a_clock_change_day_moves_to_the_next_day()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        // 2026-03-08: clocks jump from 02:00 to 03:00, so 02:30 doesn't exist that day.
        var now = new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.Zero); // 22:00 on 7 March in New York

        var next = DemoResetService.NextResetAfter(now, new TimeOnly(2, 30), zone);

        TimeZoneInfo.ConvertTime(next, zone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("2026-03-09 02:30");
    }

    [Theory]
    [InlineData("03:00", true)]
    [InlineData("23:59", true)]
    [InlineData("3:00 AM", false)]
    [InlineData("25:00", false)]
    [InlineData("", false)]
    public void The_reset_time_must_be_a_24_hour_time(string value, bool valid) =>
        DemoOptions.TryParseResetAt(value, out _).ShouldBe(valid);

    [Fact]
    public void Every_scale_describes_a_hospital_big_enough_to_use()
    {
        foreach (var scale in Enum.GetValues<DemoScale>())
        {
            var size = DemoSizes.For(scale);

            size.Patients.ShouldBeGreaterThan(0);
            size.Wards.ShouldBeInRange(3, 9);
            size.NursesPerWard.ShouldBeGreaterThanOrEqualTo(4); // enough for the demo nurse, her swap partners and cover
        }

        DemoSizes.For(DemoScale.Standard).Patients.ShouldBeGreaterThan(DemoSizes.For(DemoScale.Small).Patients);
    }

    [Fact]
    public void The_login_catalog_covers_every_default_role_once()
    {
        var roles = DemoAccountCatalog.Roles.Select(r => r.Role).ToList();

        roles.ShouldBeUnique();
        roles.ShouldBe(
            [.. Hilom.Access.Seeding.DefaultRoles.All.Select(r => r.Name).Append(Hilom.Access.Domain.Role.SystemAdministratorName)],
            ignoreOrder: true);
    }
}

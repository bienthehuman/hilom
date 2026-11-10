using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Workforce;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Scheduling;

[Collection(ApiTestGroup.Name)]
public sealed class SlotTests(HilomApiFactory factory)
{
    private const string Url = "/api/scheduling/slots";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A Monday at least a week ahead, so every slot is in the future.</summary>
    private static DateOnly NextMonday()
    {
        var day = StaffFixture.Today.AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>A physician employee, through the API. Returns their employee id.</summary>
    private static async Task<string> PhysicianAsync(StaffFixture staff, bool acceptsAppointments = true, int consultationMinutes = 30)
    {
        var admin = staff.Admin;
        async Task<string> Lookup(string list) =>
            (await (await admin.PostAsJsonAsync($"/api/workforce/{list}", new { name = $"{list} {StaffFixture.Marker()}" }, Ct))
                .ReadJsonAsync()).GetProperty("id").GetString()!;

        var response = await admin.PostAsJsonAsync(
            "/api/workforce/employees",
            new
            {
                firstName = "Jose",
                lastName = $"Santos {StaffFixture.Marker()}",
                sex = "male",
                birthDate = "1980-02-14",
                departmentId = await Lookup("departments"),
                positionId = await Lookup("positions"),
                professionalType = "physician",
                licenseNumber = "0123456",
                employmentType = "full_time",
                hiredOn = "2015-07-01",
                physician = new { specializationId = await Lookup("specializations"), acceptsAppointments, consultationMinutes },
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    private static object Pattern(string physicianId, DateOnly from, DateOnly? to = null, string[]? days = null, string start = "09:00", string end = "11:00", int? minutes = null) => new
    {
        physicianId,
        from = StaffFixture.Day(from),
        to = StaffFixture.Day(to ?? from.AddDays(6)),
        days = days ?? ["monday", "wednesday"],
        startTime = start,
        endTime = end,
        slotMinutes = minutes,
    };

    private static async Task<JsonElement> PublishAsync(HttpClient client, object pattern)
    {
        var response = await client.PostAsJsonAsync($"{Url}/publish", pattern, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement[]> ListAsync(HttpClient client, string physicianId, DateOnly from, DateOnly to) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"{Url}?physicianId={physicianId}&from={StaffFixture.Day(from)}&to={StaffFixture.Day(to)}", Ct)).EnumerateArray()];

    private static string ManilaTime(JsonElement slot, string property) =>
        TimeZoneInfo.ConvertTime(slot.GetProperty(property).GetDateTimeOffset(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"))
            .ToString("ddd HH:mm", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Front_desk_publishes_a_weekly_pattern_for_a_physician()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var physician = await PhysicianAsync(staff);
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        var monday = NextMonday();

        var result = await PublishAsync(frontDesk, Pattern(physician, monday));

        // Monday and Wednesday, 09:00–11:00 in 30-minute slots (the physician's consultation length).
        result.GetProperty("created").GetInt32().ShouldBe(8);
        result.GetProperty("skipped").GetInt32().ShouldBe(0);
        var slots = await ListAsync(frontDesk, physician, monday, monday.AddDays(6));
        slots.Select(s => ManilaTime(s, "startsAt")).Take(4).ShouldBe(["Mon 09:00", "Mon 09:30", "Mon 10:00", "Mon 10:30"]);
        ManilaTime(slots[^1], "endsAt").ShouldBe("Wed 11:00");
        slots.ShouldAllBe(s => s.GetProperty("status").GetString() == "open");

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(physician) && e.Event == AuditEvents.Scheduling.SlotsPublished.Key));
        JsonDocument.Parse(audit.Single().Details!).RootElement.GetProperty("created").GetInt32().ShouldBe(8);
    }

    [Fact]
    public async Task Publishing_again_skips_slots_that_overlap_open_ones()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var physician = await PhysicianAsync(staff);
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        var monday = NextMonday();
        await PublishAsync(frontDesk, Pattern(physician, monday, days: ["monday"]));

        // 10:00–12:00 in 20-minute slots: the three before 11:00 overlap, the three after don't.
        var again = await PublishAsync(frontDesk, Pattern(physician, monday, days: ["monday"], start: "10:00", end: "12:00", minutes: 20));

        again.GetProperty("created").GetInt32().ShouldBe(3);
        again.GetProperty("skipped").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Closing_a_slot_frees_its_time()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var physician = await PhysicianAsync(staff);
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        var monday = NextMonday();
        await PublishAsync(frontDesk, Pattern(physician, monday, days: ["monday"], end: "09:30"));
        var slot = (await ListAsync(frontDesk, physician, monday, monday)).Single();
        var id = slot.GetProperty("id").GetString();

        var close = await frontDesk.PostAsync($"{Url}/{id}/close", null, Ct);
        var again = await frontDesk.PostAsync($"{Url}/{id}/close", null, Ct);

        (await close.ReadJsonAsync()).GetProperty("status").GetString().ShouldBe("closed");
        (await again.ProblemCodeAsync()).ShouldBe("slot_not_open");
        var republished = await PublishAsync(frontDesk, Pattern(physician, monday, days: ["monday"], end: "09:30"));
        republished.GetProperty("created").GetInt32().ShouldBe(1);
        var closed = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(id!) && e.Event == AuditEvents.Scheduling.SlotClosed.Key));
        closed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_overlapping_open_slots_but_not_closed_ones()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var physician = await PhysicianAsync(staff);
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        var monday = NextMonday();
        await PublishAsync(frontDesk, Pattern(physician, monday, days: ["monday"], end: "09:30"));
        var slot = (await ListAsync(frontDesk, physician, monday, monday)).Single();
        var startsAt = slot.GetProperty("startsAt").GetDateTimeOffset();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        async Task InsertAsync(string status)
        {
            await using var command = new NpgsqlCommand(
                """
                insert into scheduling.availability_slots (id, physician_id, during, status, created_at, updated_at)
                values (gen_random_uuid(), @physician, tstzrange(@start, @end), @status, now(), now())
                """,
                connection);
            command.Parameters.AddWithValue("physician", Guid.Parse(physician));
            command.Parameters.AddWithValue("start", startsAt.AddMinutes(15).UtcDateTime);
            command.Parameters.AddWithValue("end", startsAt.AddMinutes(45).UtcDateTime);
            command.Parameters.AddWithValue("status", status);
            await command.ExecuteNonQueryAsync(Ct);
        }

        var error = await Should.ThrowAsync<PostgresException>(() => InsertAsync("open"));
        error.ConstraintName.ShouldBe("ex_availability_slots_no_overlap");
        await InsertAsync("closed");
    }

    [Fact]
    public async Task Slots_belong_to_a_physician_profile()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var nurse = await staff.NurseAsync();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            insert into scheduling.availability_slots (id, physician_id, during, status, created_at, updated_at)
            values (gen_random_uuid(), @nurse, tstzrange(now() + interval '1 day', now() + interval '25 hours'), 'open', now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("nurse", Guid.Parse(nurse));

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("fk_availability_slots_physician_profiles_physician_id");
    }

    [Fact]
    public async Task A_physician_manages_only_their_own_slots()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var mine = await PhysicianAsync(staff);
        var colleague = await PhysicianAsync(staff);
        var doctor = await staff.SignInAsEmployeeAsync(mine, "Physician");
        var monday = NextMonday();

        var physicians = await doctor.GetFromJsonAsync<JsonElement>("/api/scheduling/physicians", Ct);
        var own = await doctor.PostAsJsonAsync($"{Url}/publish", Pattern(mine, monday, days: ["monday"]), Ct);
        var other = await doctor.PostAsJsonAsync($"{Url}/publish", Pattern(colleague, monday, days: ["monday"]), Ct);
        var list = await doctor.GetAsync($"{Url}?physicianId={colleague}&from={StaffFixture.Day(monday)}&to={StaffFixture.Day(monday)}", Ct);

        physicians.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ShouldBe([mine]);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.ProblemCodeAsync()).ShouldBe("physician_not_found");
        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(colleague) && e.Event == AuditEvents.Access.Denied.Key));
        denied.Count.ShouldBe(2);

        // A colleague's slot can't be closed either.
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        await PublishAsync(frontDesk, Pattern(colleague, monday, days: ["monday"], end: "09:30"));
        var theirs = (await ListAsync(frontDesk, colleague, monday, monday)).Single().GetProperty("id").GetString();
        (await doctor.PostAsync($"{Url}/{theirs}/close", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Physicians_who_do_not_take_appointments_cannot_publish()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var physician = await PhysicianAsync(staff, acceptsAppointments: false);
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");

        var response = await frontDesk.PostAsJsonAsync($"{Url}/publish", Pattern(physician, NextMonday()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ProblemCodeAsync()).ShouldBe("physician_not_bookable");
    }

    [Fact]
    public async Task Patterns_are_validated()
    {
        var staff = await StaffFixture.CreateAsync(factory);
        var physician = await PhysicianAsync(staff);
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        var monday = NextMonday();

        async Task<JsonElement> Errors(object pattern)
        {
            var response = await frontDesk.PostAsJsonAsync($"{Url}/publish", pattern, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await response.ReadJsonAsync()).GetProperty("errors");
        }

        (await Errors(Pattern(physician, StaffFixture.Today.AddDays(-1)))).GetProperty("from")[0].GetString().ShouldBe("Start today or later.");
        (await Errors(Pattern(physician, monday, start: "11:00", end: "09:00"))).GetProperty("endTime")[0].GetString()
            .ShouldBe("The end time must be after the start time.");
        (await Errors(Pattern(physician, monday, days: []))).GetProperty("days")[0].GetString().ShouldBe("Choose at least one day of the week.");
        (await Errors(Pattern(physician, monday, to: monday.AddDays(90)))).GetProperty("to")[0].GetString().ShouldBe("Publish at most 12 weeks at a time.");
        (await Errors(Pattern(physician, monday, start: "09:00", end: "09:20"))).GetProperty("endTime")[0].GetString()
            .ShouldStartWith("These choices make no slots.");

        // 12 weeks, every day, 07:00–19:00 in 15-minute slots is far more than 500.
        string[] everyDay = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
        (await Errors(Pattern(physician, monday, monday.AddDays(80), everyDay, "07:00", "19:00", 15))).GetProperty("to")[0].GetString()
            .ShouldNotBeNull().ShouldContain("at most 500");
    }

    [Fact]
    public async Task Staff_without_the_permission_get_403()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        (await hr.GetAsync("/api/scheduling/physicians", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nurse.PostAsJsonAsync($"{Url}/publish", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

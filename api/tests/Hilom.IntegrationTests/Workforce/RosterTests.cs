using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;
using static Hilom.IntegrationTests.Workforce.StaffFixture;

namespace Hilom.IntegrationTests.Workforce;

/// <summary>The duty roster: one shift per day, only on a ward you're assigned to, scoped by ward and own.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class RosterTests(HilomApiFactory factory)
{
    private const string Url = "/api/workforce/roster";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, string employeeId, string wardId, string shiftId, DateOnly day) =>
        client.PostAsJsonAsync($"{Url}/entries", new { employeeId, wardId, shiftId, dutyDate = Day(day) }, Ct);

    private static async Task<JsonElement> AddedAsync(HttpClient client, string employeeId, string wardId, string shiftId, DateOnly day)
    {
        var response = await AddAsync(client, employeeId, wardId, shiftId, day);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    [Fact]
    public async Task A_nurse_on_the_ward_is_rostered_and_sees_it_in_their_schedule()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync();
        var am = await staff.ShiftAsync();
        await staff.AssignAsync(nurse, ward, Today.AddDays(-3));

        var entry = await AddedAsync(staff.Admin, nurse, ward, am, Today.AddDays(1));
        entry.GetProperty("status").GetString().ShouldBe("scheduled");
        entry.GetProperty("shift").GetProperty("id").GetString().ShouldBe(am);

        var grid = await staff.Admin.GetFromJsonAsync<JsonElement>($"{Url}/wards/{ward}?from={Day(Today)}&to={Day(Today.AddDays(6))}", Ct);
        var row = grid.GetProperty("staff")[0];
        row.GetProperty("employee").GetProperty("id").GetString().ShouldBe(nurse);
        row.GetProperty("assignments")[0].GetProperty("startsOn").GetString().ShouldBe(Day(Today.AddDays(-3)));
        grid.GetProperty("entries")[0].GetProperty("dutyDate").GetString().ShouldBe(Day(Today.AddDays(1)));

        var client = await staff.SignInAsEmployeeAsync(nurse, "Staff Nurse");
        var mine = await client.GetFromJsonAsync<JsonElement>($"{Url}/mine?from={Day(Today)}&to={Day(Today.AddDays(6))}", Ct);
        mine.GetProperty("linked").GetBoolean().ShouldBeTrue();
        mine.GetProperty("entries")[0].GetProperty("ward").GetProperty("id").GetString().ShouldBe(ward);
    }

    [Fact]
    public async Task One_shift_per_day()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync();
        var am = await staff.ShiftAsync();
        var pm = await staff.ShiftAsync("14:00", "22:00");
        await staff.AssignAsync(nurse, ward, Today);
        await AddedAsync(staff.Admin, nurse, ward, am, Today);

        var second = await AddAsync(staff.Admin, nurse, ward, pm, Today);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.ReadJsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("already_rostered");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("One shift per day");
        await AddedAsync(staff.Admin, nurse, ward, pm, Today.AddDays(1));
    }

    [Fact]
    public async Task The_database_also_allows_one_scheduled_shift_per_day()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync();
        var am = await staff.ShiftAsync();
        await staff.AssignAsync(nurse, ward, Today);
        await AddedAsync(staff.Admin, nurse, ward, am, Today);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            insert into workforce.roster_entries (id, employee_id, ward_id, shift_id, duty_date, status, created_at, updated_at)
            values (gen_random_uuid(), @employee, @ward, @shift, @day, 'scheduled', now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("employee", Guid.Parse(nurse));
        command.Parameters.AddWithValue("ward", Guid.Parse(ward));
        command.Parameters.AddWithValue("shift", Guid.Parse(am));
        command.Parameters.AddWithValue("day", Today);

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("ux_roster_entries_one_shift_per_day");
    }

    [Fact]
    public async Task Only_staff_assigned_to_the_ward_that_day_can_be_rostered()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync();
        var am = await staff.ShiftAsync();
        await staff.AssignAsync(nurse, ward, Today.AddDays(2), Today.AddDays(4));

        var before = await AddAsync(staff.Admin, nurse, ward, am, Today);
        var after = await AddAsync(staff.Admin, nurse, ward, am, Today.AddDays(5));

        before.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await before.ProblemCodeAsync()).ShouldBe("not_assigned_to_ward");
        (await after.ProblemCodeAsync()).ShouldBe("not_assigned_to_ward");
        await AddedAsync(staff.Admin, nurse, ward, am, Today.AddDays(4));
    }

    [Fact]
    public async Task Entries_change_shift_and_are_removed_with_an_audit_trail()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync();
        var am = await staff.ShiftAsync();
        var pm = await staff.ShiftAsync("14:00", "22:00");
        await staff.AssignAsync(nurse, ward, Today);
        var entry = await AddedAsync(staff.Admin, nurse, ward, am, Today);
        var url = $"{Url}/entries/{entry.GetProperty("id").GetString()}";
        var version = entry.GetProperty("version").GetUInt32();

        var changed = await staff.Admin.PutAsJsonAsync(url, new { shiftId = pm, version }, Ct);
        var stale = await staff.Admin.PutAsJsonAsync(url, new { shiftId = am, version }, Ct);
        (await changed.ReadJsonAsync()).GetProperty("shift").GetProperty("id").GetString().ShouldBe(pm);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");

        (await staff.Admin.DeleteAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await staff.Admin.DeleteAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var id = Guid.Parse(entry.GetProperty("id").GetString()!);
        var events = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.Event == AuditEvents.Workforce.RosterChanged.Key));
        events.Select(e => JsonDocument.Parse(e.Details!).RootElement.GetProperty("change").GetString())
            .ShouldBe(["removed", "shift_changed", "added"]);
        events.ShouldAllBe(e => e.Severity == AuditSeverity.Info);
    }

    [Fact]
    public async Task Inactive_shifts_and_long_ranges_are_rejected()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync();
        var retired = await staff.ShiftAsync();
        await staff.AssignAsync(nurse, ward, Today);
        (await staff.Admin.PostAsync($"/api/workforce/shifts/{retired}/deactivate", null, Ct)).EnsureSuccessStatusCode();

        var inactive = await AddAsync(staff.Admin, nurse, ward, retired, Today);
        var tooLong = await staff.Admin.GetAsync($"{Url}/wards/{ward}?from={Day(Today)}&to={Day(Today.AddDays(31))}", Ct);
        var backwards = await staff.Admin.GetAsync($"{Url}/mine?from={Day(Today)}&to={Day(Today.AddDays(-1))}", Ct);

        (await inactive.ReadJsonAsync()).GetProperty("errors").GetProperty("shiftId")[0].GetString().ShouldBe("Choose an active shift.");
        (await tooLong.ProblemCodeAsync()).ShouldBe("roster_range_invalid");
        backwards.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_default_range_is_this_week_monday_to_sunday()
    {
        var staff = await CreateAsync(factory);

        var grid = await staff.Admin.GetFromJsonAsync<JsonElement>($"{Url}/wards/{await staff.WardAsync()}", Ct);
        var from = DateOnly.Parse(grid.GetProperty("from").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var to = DateOnly.Parse(grid.GetProperty("to").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        from.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        to.ShouldBe(from.AddDays(6));
        Today.ShouldBeInRange(from, to);
    }

    [Fact]
    public async Task A_head_nurse_sees_and_manages_only_their_wards_roster()
    {
        var staff = await CreateAsync(factory);
        var mine = await staff.WardAsync();
        var other = await staff.WardAsync();
        var head = await staff.NurseAsync("Head");
        var colleague = await staff.NurseAsync("Colleague");
        var elsewhere = await staff.NurseAsync("Elsewhere");
        var am = await staff.ShiftAsync();
        await staff.AssignAsync(head, mine, Today.AddDays(-7));
        await staff.AssignAsync(colleague, mine, Today);
        await staff.AssignAsync(elsewhere, other, Today);
        var otherEntry = await AddedAsync(staff.Admin, elsewhere, other, am, Today);
        var client = await staff.SignInAsEmployeeAsync(head, "Head Nurse");

        var wards = await client.GetFromJsonAsync<JsonElement>($"{Url}/wards", Ct);
        wards.EnumerateArray().Select(w => w.GetProperty("id").GetString()).ShouldBe([mine]);
        (await client.GetAsync($"{Url}/wards/{mine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"{Url}/wards/{other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await AddedAsync(client, colleague, mine, am, Today);
        (await AddAsync(client, elsewhere, other, am, Today.AddDays(1))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"{Url}/entries/{otherEntry.GetProperty("id").GetString()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == AuditEvents.Access.Denied.Key && e.EntityId == Guid.Parse(other)));
        denied.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Staff_nurses_read_their_wards_roster_and_physicians_only_their_own()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var nurse = await staff.NurseAsync("Nurse");
        var doctor = await staff.NurseAsync("Doctor");
        var am = await staff.ShiftAsync();
        await staff.AssignAsync(nurse, ward, Today);
        await staff.AssignAsync(doctor, ward, Today);
        await AddedAsync(staff.Admin, doctor, ward, am, Today);
        var nurseClient = await staff.SignInAsEmployeeAsync(nurse, "Staff Nurse");
        var doctorClient = await staff.SignInAsEmployeeAsync(doctor, "Physician");

        (await nurseClient.GetAsync($"{Url}/wards/{ward}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AddAsync(nurseClient, nurse, ward, am, Today.AddDays(1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await doctorClient.GetAsync($"{Url}/wards/{ward}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await doctorClient.GetFromJsonAsync<JsonElement>($"{Url}/wards", Ct)).GetArrayLength().ShouldBe(0);
        var mine = await doctorClient.GetFromJsonAsync<JsonElement>($"{Url}/mine", Ct);
        mine.GetProperty("entries").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Accounts_without_an_employee_record_have_no_schedule()
    {
        var staff = await CreateAsync(factory);

        var mine = await staff.Admin.GetFromJsonAsync<JsonElement>($"{Url}/mine", Ct);

        mine.GetProperty("linked").GetBoolean().ShouldBeFalse();
        mine.GetProperty("entries").GetArrayLength().ShouldBe(0);
    }
}

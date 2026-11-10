using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Workforce;

/// <summary>Ward assignments and the <c>ward</c> scope they drive (docs/RBAC.md).</summary>
[Collection(ApiTestGroup.Name)]
public sealed class WardAssignmentTests(HilomApiFactory factory)
{
    private const string Url = "/api/workforce/ward-assignments";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Today in hospital time, as the API sees it.</summary>
    private static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")).DateTime);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Marker() => $"A{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant();

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    private static async Task<string> WardAsync(HttpClient admin, string? code = null)
    {
        var response = await admin.PostAsJsonAsync("/api/facility/wards", new { code = code ?? Marker(), name = "Ward", wardType = "general" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    private static async Task<string> NurseAsync(HttpClient admin, string lastName = "Reyes")
    {
        async Task<string> Lookup(string list) =>
            (await (await admin.PostAsJsonAsync($"/api/workforce/{list}", new { name = $"{list} {Marker()}" }, Ct)).ReadJsonAsync())
                .GetProperty("id").GetString()!;

        var response = await admin.PostAsJsonAsync(
            "/api/workforce/employees",
            new
            {
                firstName = "Ana",
                lastName,
                sex = "female",
                birthDate = "1995-04-12",
                departmentId = await Lookup("departments"),
                positionId = await Lookup("positions"),
                professionalType = "nurse",
                licenseNumber = "0456789",
                employmentType = "full_time",
                hiredOn = "2020-06-01",
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, string employeeId, string wardId, DateOnly startsOn, DateOnly? lastDay = null) =>
        client.PostAsJsonAsync(
            Url,
            new { employeeId, wardId, startsOn = Day(startsOn), lastDay = lastDay is { } d ? Day(d) : null, reason = "Staffing" },
            Ct);

    private static async Task<JsonElement> AssignedAsync(HttpClient client, string employeeId, string wardId, DateOnly startsOn, DateOnly? lastDay = null)
    {
        var response = await AssignAsync(client, employeeId, wardId, startsOn, lastDay);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static async Task EndAsync(HttpClient client, JsonElement assignment, DateOnly lastDay)
    {
        var response = await client.PostAsJsonAsync(
            $"{Url}/{assignment.GetProperty("id").GetString()}/end",
            new { lastDay = Day(lastDay), version = assignment.GetProperty("version").GetUInt32() },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string[]> ListedEmployeesAsync(HttpClient client, string query = "") =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"{Url}?pageSize=100{query}", Ct)).GetProperty("items")
            .EnumerateArray().Select(a => a.GetProperty("employee").GetProperty("id").GetString()!)];

    /// <summary>Signs in a user holding the role, linked to the employee (as accepting an invite does).</summary>
    private async Task<HttpClient> SignInAsEmployeeAsync(string employeeId, string role)
    {
        const string password = "Ward-Scope-Test-2026";
        var user = await factory.CreateUserAsync(password, disabled: false, role);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("update workforce.employees set user_id = @user where id = @id", connection);
        command.Parameters.AddWithValue("user", user.Id);
        command.Parameters.AddWithValue("id", Guid.Parse(employeeId));
        await command.ExecuteNonQueryAsync(Ct);

        var client = factory.CreateBrowserClient();
        await client.SignInAsync(user.Email, password);
        return client;
    }

    /// <summary>A role granting the permissions at the ward scope, created through the API like an admin would.</summary>
    private static async Task<string> WardScopedRoleAsync(HttpClient admin, params string[] permissions)
    {
        var name = $"Ward role {Marker()}";
        var response = await admin.PostAsJsonAsync(
            "/api/access/roles",
            new { name, description = "", grants = permissions.Select(p => new { permission = p, scope = "ward" }) },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return name;
    }

    [Fact]
    public async Task Staff_are_on_one_ward_at_a_time()
    {
        var admin = await AdminAsync();
        var ward3 = await WardAsync(admin, $"{Marker()}-3");
        var ward5 = await WardAsync(admin);
        var nurse = await NurseAsync(admin);

        var first = await AssignedAsync(admin, nurse, ward3, Today.AddDays(-10));
        first.GetProperty("state").GetString().ShouldBe("current");
        first.GetProperty("lastDay").ValueKind.ShouldBe(JsonValueKind.Null);

        var overlap = await AssignAsync(admin, nurse, ward5, Today);
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await overlap.ReadJsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("assignment_overlaps");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("with no end date");

        // Moving wards today: the old assignment ends yesterday, the new one starts today.
        await EndAsync(admin, first, Today.AddDays(-1));
        var moved = await AssignedAsync(admin, nurse, ward5, Today);

        var history = (await admin.GetFromJsonAsync<JsonElement>($"{Url}?employeeId={nurse}", Ct)).GetProperty("items");
        history.EnumerateArray().Select(a => a.GetProperty("state").GetString()).ShouldBe(["current", "past"]);
        history[1].GetProperty("lastDay").GetString().ShouldBe(Day(Today.AddDays(-1)));
        moved.GetProperty("assignedBy").GetString().ShouldBe("System Administrator");

        var events = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == AuditEvents.Workforce.WardAssignmentChanged.Key
            && (e.EntityId == Guid.Parse(first.GetProperty("id").GetString()!) || e.EntityId == Guid.Parse(moved.GetProperty("id").GetString()!))));
        events.Count.ShouldBe(3);
        events.ShouldAllBe(e => e.Severity == AuditSeverity.Critical);
    }

    [Fact]
    public async Task The_database_refuses_overlapping_assignments_too()
    {
        var admin = await AdminAsync();
        var nurse = await NurseAsync(admin);
        await AssignedAsync(admin, nurse, await WardAsync(admin), Today, Today.AddDays(30));
        var other = await WardAsync(admin);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            insert into workforce.ward_assignments (id, employee_id, ward_id, period, created_at, updated_at)
            values (gen_random_uuid(), @employee, @ward, daterange(@from, null), now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("employee", Guid.Parse(nurse));
        command.Parameters.AddWithValue("ward", Guid.Parse(other));
        command.Parameters.AddWithValue("from", Today.AddDays(29));

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("ex_ward_assignments_one_ward_at_a_time");
    }

    [Fact]
    public async Task A_head_nurse_sees_their_wards_assignments_and_follows_a_move()
    {
        var admin = await AdminAsync();
        var ward3 = await WardAsync(admin);
        var ward5 = await WardAsync(admin);
        var headNurse = await NurseAsync(admin, "Head");
        var colleague3 = await NurseAsync(admin, "OnWard3");
        var colleague5 = await NurseAsync(admin, "OnWard5");
        var head = await AssignedAsync(admin, headNurse, ward3, Today.AddDays(-30));
        await AssignedAsync(admin, colleague3, ward3, Today);
        await AssignedAsync(admin, colleague5, ward5, Today);
        var client = await SignInAsEmployeeAsync(headNurse, "Head Nurse");

        var onWard3 = await ListedEmployeesAsync(client);
        onWard3.ShouldContain(colleague3);
        onWard3.ShouldContain(headNurse);
        onWard3.ShouldNotContain(colleague5);

        // Reassigned to Ward 5 from today: access follows on the next request, no permission edits.
        await EndAsync(admin, head, Today.AddDays(-1));
        await AssignedAsync(admin, headNurse, ward5, Today);

        var onWard5 = await ListedEmployeesAsync(client, $"&on={Day(Today)}");
        onWard5.ShouldContain(colleague5);
        onWard5.ShouldContain(headNurse);
        onWard5.ShouldNotContain(colleague3);
    }

    [Fact]
    public async Task Staff_nurses_see_only_their_own_assignments()
    {
        var admin = await AdminAsync();
        var ward = await WardAsync(admin);
        var self = await NurseAsync(admin, "Self");
        var colleague = await NurseAsync(admin, "Colleague");
        await AssignedAsync(admin, self, ward, Today);
        await AssignedAsync(admin, colleague, ward, Today);
        var client = await SignInAsEmployeeAsync(self, "Staff Nurse");

        (await ListedEmployeesAsync(client)).ShouldBe([self]);
        (await AssignAsync(client, self, ward, Today.AddDays(60))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unassigned_ward_scoped_staff_see_nothing()
    {
        var admin = await AdminAsync();
        var headNurse = await NurseAsync(admin, "Unassigned");
        await AssignedAsync(admin, await NurseAsync(admin), await WardAsync(admin), Today);
        var client = await SignInAsEmployeeAsync(headNurse, "Head Nurse");

        (await ListedEmployeesAsync(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ward_scoped_managers_assign_only_to_their_own_wards()
    {
        var admin = await AdminAsync();
        var mine = await WardAsync(admin);
        var theirs = await WardAsync(admin);
        var manager = await NurseAsync(admin, "Manager");
        var newcomer = await NurseAsync(admin, "Newcomer");
        var elsewhere = await AssignedAsync(admin, await NurseAsync(admin, "Elsewhere"), theirs, Today);
        await AssignedAsync(admin, manager, mine, Today.AddDays(-5));
        var role = await WardScopedRoleAsync(admin, "workforce.assignments.view", "workforce.assignments.manage");
        var client = await SignInAsEmployeeAsync(manager, role);

        var outside = await AssignAsync(client, newcomer, theirs, Today);
        var endOutside = await client.PostAsJsonAsync(
            $"{Url}/{elsewhere.GetProperty("id").GetString()}/end",
            new { lastDay = Day(Today), version = elsewhere.GetProperty("version").GetUInt32() },
            Ct);
        var inside = await AssignAsync(client, newcomer, mine, Today);

        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outside.ProblemCodeAsync()).ShouldBe("ward_not_found");
        endOutside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        inside.StatusCode.ShouldBe(HttpStatusCode.Created);

        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == AuditEvents.Access.Denied.Key && e.EntityId == Guid.Parse(theirs)));
        denied.Single().Details.ShouldNotBeNull().ShouldContain("out_of_scope");
    }

    [Fact]
    public async Task Dates_wards_and_people_are_checked()
    {
        var admin = await AdminAsync();
        var ward = await WardAsync(admin);
        var nurse = await NurseAsync(admin);

        var backwards = await AssignAsync(admin, nurse, ward, Today, Today.AddDays(-1));
        var unknownWard = await AssignAsync(admin, nurse, Guid.CreateVersion7().ToString(), Today);
        var assignment = await AssignedAsync(admin, nurse, ward, Today, Today.AddDays(10));
        var extend = await admin.PostAsJsonAsync(
            $"{Url}/{assignment.GetProperty("id").GetString()}/end",
            new { lastDay = Day(Today.AddDays(20)), version = assignment.GetProperty("version").GetUInt32() },
            Ct);
        (await admin.PostAsync($"/api/facility/wards/{ward}/deactivate", null, Ct)).EnsureSuccessStatusCode();
        var inactiveWard = await AssignAsync(admin, await NurseAsync(admin), ward, Today);

        (await backwards.ReadJsonAsync()).GetProperty("errors").GetProperty("lastDay")[0].GetString().ShouldBe("The last day can't be before the start.");
        (await unknownWard.ReadJsonAsync()).GetProperty("errors").GetProperty("wardId")[0].GetString().ShouldBe("Choose an active ward.");
        (await extend.ReadJsonAsync()).GetProperty("errors").GetProperty("lastDay")[0].GetString().ShouldNotBeNull().ShouldContain("already ends earlier");
        inactiveWard.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Separated_employees_cant_be_assigned()
    {
        var admin = await AdminAsync();
        var nurse = await NurseAsync(admin);
        var employee = await admin.GetFromJsonAsync<JsonElement>($"/api/workforce/employees/{nurse}", Ct);
        (await admin.PostAsJsonAsync(
            $"/api/workforce/employees/{nurse}/status",
            new { status = "separated", separatedOn = Day(Today), version = employee.GetProperty("version").GetUInt32() },
            Ct)).EnsureSuccessStatusCode();

        var response = await AssignAsync(admin, nurse, await WardAsync(admin), Today);

        (await response.ProblemCodeAsync()).ShouldBe("employee_separated");
    }

    [Fact]
    public async Task Chief_nurses_manage_assignments_but_staff_nurses_dont()
    {
        var admin = await AdminAsync();
        var (chief, _) = await factory.SignInWithRolesAsync("Chief Nurse");
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var ward = await WardAsync(admin);

        (await AssignAsync(nurse, await NurseAsync(admin), ward, Today)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssignedAsync(chief, await NurseAsync(admin), ward, Today);
    }
}

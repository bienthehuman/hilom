using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Workforce;

[Collection(ApiTestGroup.Name)]
public sealed partial class EmployeeTests(HilomApiFactory factory)
{
    private const string Url = "/api/workforce/employees";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Marker() => Guid.NewGuid().ToString("N")[..8];

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    private static async Task<string> LookupAsync(HttpClient admin, string list, string name)
    {
        var response = await admin.PostAsJsonAsync($"/api/workforce/{list}", new { name = $"{name} {Marker()}" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    private sealed record Lookups(string Department, string Position, string Specialization);

    private static async Task<Lookups> LookupsAsync(HttpClient admin) =>
        new(
            await LookupAsync(admin, "departments", "Nursing Service"),
            await LookupAsync(admin, "positions", "Staff Nurse"),
            await LookupAsync(admin, "specializations", "Cardiology"));

    private static Dictionary<string, object?> Nurse(Lookups lookups, string lastName = "Reyes") => new()
    {
        ["firstName"] = "Ana",
        ["middleName"] = "Santos",
        ["lastName"] = lastName,
        ["sex"] = "female",
        ["birthDate"] = "1995-04-12",
        ["contactNumber"] = "0917 555 0101",
        ["email"] = "ana.reyes@hospital.test",
        ["departmentId"] = lookups.Department,
        ["positionId"] = lookups.Position,
        ["professionalType"] = "nurse",
        ["licenseNumber"] = "0456789",
        ["employmentType"] = "full_time",
        ["hiredOn"] = "2024-06-01",
        ["physician"] = null,
    };

    private static async Task<JsonElement> CreateAsync(HttpClient admin, Dictionary<string, object?> body)
    {
        var response = await admin.PostAsJsonAsync(Url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    /// <summary>Links an employee to a sign-in account, as accepting an invite will (slice 1.3).</summary>
    private async Task LinkUserAsync(string employeeId, Guid userId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("update workforce.employees set user_id = @user where id = @id", connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("id", Guid.Parse(employeeId));
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task HR_creates_a_nurse_with_a_generated_employee_number()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var lookups = await LookupsAsync(await AdminAsync());

        var created = await CreateAsync(hr, Nurse(lookups));

        EmployeeNumber().IsMatch(created.GetProperty("employeeNo").GetString()!).ShouldBeTrue();
        created.GetProperty("status").GetString().ShouldBe("active");
        created.GetProperty("department").GetProperty("id").GetString().ShouldBe(lookups.Department);
        created.GetProperty("hasAccount").GetBoolean().ShouldBeFalse();
        created.GetProperty("physician").ValueKind.ShouldBe(JsonValueKind.Null);

        var id = Guid.Parse(created.GetProperty("id").GetString()!);
        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.Event == AuditEvents.Workforce.EmployeeCreated.Key));
        audit.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Physicians_and_nurses_need_a_license_number()
    {
        var admin = await AdminAsync();
        var body = Nurse(await LookupsAsync(admin));
        body["licenseNumber"] = "  ";

        var response = await admin.PostAsJsonAsync(Url, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").GetProperty("licenseNumber")[0].GetString()
            .ShouldBe("Physicians and nurses need a license number.");
    }

    [Fact]
    public async Task The_database_also_refuses_a_nurse_without_a_license()
    {
        var admin = await AdminAsync();
        var nurse = await CreateAsync(admin, Nurse(await LookupsAsync(admin)));

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("update workforce.employees set license_number = null where id = @id", connection);
        command.Parameters.AddWithValue("id", Guid.Parse(nurse.GetProperty("id").GetString()!));

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("ck_employees_license_number");
    }

    [Fact]
    public async Task Physicians_have_a_physician_profile_and_nobody_else_does()
    {
        var admin = await AdminAsync();
        var lookups = await LookupsAsync(admin);
        var doctor = Nurse(lookups, "Santos");
        doctor["professionalType"] = "physician";

        var withoutProfile = await admin.PostAsJsonAsync(Url, doctor, Ct);
        doctor["physician"] = new { specializationId = lookups.Specialization, acceptsAppointments = true };
        var created = await CreateAsync(admin, doctor);
        var nurse = Nurse(lookups);
        nurse["physician"] = new { specializationId = lookups.Specialization, acceptsAppointments = true };
        var nurseWithProfile = await admin.PostAsJsonAsync(Url, nurse, Ct);

        (await withoutProfile.ReadJsonAsync()).GetProperty("errors").GetProperty("physician")[0].GetString()
            .ShouldBe("Add the physician's specialization.");
        var physician = created.GetProperty("physician");
        physician.GetProperty("specialization").GetProperty("id").GetString().ShouldBe(lookups.Specialization);
        physician.GetProperty("consultationMinutes").GetInt32().ShouldBe(30);
        nurseWithProfile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_a_physician_to_another_type_removes_the_profile()
    {
        var admin = await AdminAsync();
        var lookups = await LookupsAsync(admin);
        var doctor = Nurse(lookups, "Santos");
        doctor["professionalType"] = "physician";
        doctor["physician"] = new { specializationId = lookups.Specialization, acceptsAppointments = false, consultationMinutes = 20 };
        var created = await CreateAsync(admin, doctor);

        doctor["professionalType"] = "administrative";
        doctor["physician"] = null;
        doctor["version"] = created.GetProperty("version").GetUInt32();
        var updated = await admin.PutAsJsonAsync($"{Url}/{created.GetProperty("id").GetString()}", doctor, Ct);

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.ReadJsonAsync()).GetProperty("physician").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Inactive_lookups_cant_be_chosen_but_existing_ones_are_kept()
    {
        var admin = await AdminAsync();
        var lookups = await LookupsAsync(admin);
        var created = await CreateAsync(admin, Nurse(lookups));
        (await admin.PostAsync($"/api/workforce/departments/{lookups.Department}/deactivate", null, Ct)).EnsureSuccessStatusCode();

        var newEmployee = await admin.PostAsJsonAsync(Url, Nurse(lookups), Ct);
        var edit = Nurse(lookups, "Reyes-Cruz");
        edit["version"] = created.GetProperty("version").GetUInt32();
        var edited = await admin.PutAsJsonAsync($"{Url}/{created.GetProperty("id").GetString()}", edit, Ct);

        (await newEmployee.ReadJsonAsync()).GetProperty("errors").GetProperty("departmentId")[0].GetString()
            .ShouldBe("Choose a department from the list.");
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        var department = (await edited.ReadJsonAsync()).GetProperty("department");
        department.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Saving_with_a_stale_version_is_rejected()
    {
        var admin = await AdminAsync();
        var lookups = await LookupsAsync(admin);
        var created = await CreateAsync(admin, Nurse(lookups));
        var url = $"{Url}/{created.GetProperty("id").GetString()}";
        var body = Nurse(lookups, "Reyes-Cruz");
        body["version"] = created.GetProperty("version").GetUInt32();

        (await admin.PutAsJsonAsync(url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await admin.PutAsJsonAsync(url, body, Ct);

        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Separating_needs_a_date_and_is_audited_as_critical()
    {
        var admin = await AdminAsync();
        var created = await CreateAsync(admin, Nurse(await LookupsAsync(admin)));
        var id = created.GetProperty("id").GetString();
        var version = created.GetProperty("version").GetUInt32();

        var noDate = await admin.PostAsJsonAsync($"{Url}/{id}/status", new { status = "separated", version }, Ct);
        var beforeHire = await admin.PostAsJsonAsync($"{Url}/{id}/status", new { status = "separated", separatedOn = "2020-01-01", version }, Ct);
        var separated = await admin.PostAsJsonAsync($"{Url}/{id}/status", new { status = "separated", separatedOn = "2026-10-31", version }, Ct);

        (await noDate.ReadJsonAsync()).GetProperty("errors").GetProperty("separatedOn")[0].GetString().ShouldBe("Enter the last day of work.");
        beforeHire.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        separated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await separated.ReadJsonAsync();
        body.GetProperty("status").GetString().ShouldBe("separated");
        body.GetProperty("separatedOn").GetString().ShouldBe("2026-10-31");

        var rehired = await admin.PostAsJsonAsync(
            $"{Url}/{id}/status",
            new { status = "active", separatedOn = "2026-10-31", version = body.GetProperty("version").GetUInt32() },
            Ct);
        (await rehired.ReadJsonAsync()).GetProperty("separatedOn").ValueKind.ShouldBe(JsonValueKind.Null);

        var employeeId = Guid.Parse(id!);
        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == employeeId && e.Event == AuditEvents.Workforce.EmployeeStatusChanged.Key));
        audit.Count.ShouldBe(2);
        audit.ShouldAllBe(e => e.Severity == AuditSeverity.Critical);
    }

    [Fact]
    public async Task Lists_filter_by_department_type_status_and_search()
    {
        var admin = await AdminAsync();
        var lookups = await LookupsAsync(admin);
        var marker = Marker();
        await CreateAsync(admin, Nurse(lookups, $"Alpha{marker}"));
        var leaving = await CreateAsync(admin, Nurse(lookups, $"Bravo{marker}"));
        var clerk = Nurse(lookups, $"Charlie{marker}");
        clerk["professionalType"] = "administrative";
        clerk["licenseNumber"] = null;
        await CreateAsync(admin, clerk);
        await admin.PostAsJsonAsync(
            $"{Url}/{leaving.GetProperty("id").GetString()}/status",
            new { status = "on_leave", version = leaving.GetProperty("version").GetUInt32() },
            Ct);

        async Task<string[]> NamesAsync(string query) =>
            [.. (await admin.GetFromJsonAsync<JsonElement>($"{Url}?{query}", Ct)).GetProperty("items")
                .EnumerateArray().Select(e => e.GetProperty("lastName").GetString()!)];

        (await NamesAsync($"departmentId={lookups.Department}")).ShouldBe([$"Alpha{marker}", $"Bravo{marker}", $"Charlie{marker}"]);
        (await NamesAsync($"departmentId={lookups.Department}&professionalType=nurse&status=active")).ShouldBe([$"Alpha{marker}"]);
        (await NamesAsync($"q=ana%20charlie{marker}")).ShouldBe([$"Charlie{marker}"]);
        (await NamesAsync($"q={marker}&status=on_leave")).ShouldBe([$"Bravo{marker}"]);
    }

    [Fact]
    public async Task Staff_with_own_scope_see_only_their_own_record()
    {
        var admin = await AdminAsync();
        var lookups = await LookupsAsync(admin);
        var (nurseClient, nurseUser) = await factory.SignInWithRolesAsync("Staff Nurse");
        var self = await CreateAsync(admin, Nurse(lookups, "Self"));
        var colleague = await CreateAsync(admin, Nurse(lookups, "Colleague"));
        await LinkUserAsync(self.GetProperty("id").GetString()!, nurseUser.Id);

        var list = await nurseClient.GetFromJsonAsync<JsonElement>(Url, Ct);
        var own = await nurseClient.GetAsync($"{Url}/{self.GetProperty("id").GetString()}", Ct);
        var other = await nurseClient.GetAsync($"{Url}/{colleague.GetProperty("id").GetString()}", Ct);
        var create = await nurseClient.PostAsJsonAsync(Url, Nurse(lookups), Ct);

        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe(self.GetProperty("id").GetString());
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.ReadJsonAsync()).GetProperty("hasAccount").GetBoolean().ShouldBeTrue();
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.ProblemCodeAsync()).ShouldBe("employee_not_found");
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var colleagueId = Guid.Parse(colleague.GetProperty("id").GetString()!);
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == colleagueId && e.Event == AuditEvents.Access.Denied.Key));
        denied.Single().UserId.ShouldBe(nurseUser.Id);
        denied.Single().Details.ShouldNotBeNull().ShouldContain("out_of_scope");
    }

    [Fact]
    public async Task A_nurse_without_a_linked_record_sees_none()
    {
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        var list = await nurse.GetFromJsonAsync<JsonElement>(Url, Ct);

        list.GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_employees_are_not_found()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [GeneratedRegex(@"^EMP-\d{4}-\d{4,}$")]
    private static partial Regex EmployeeNumber();
}

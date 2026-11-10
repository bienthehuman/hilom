using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Npgsql;

namespace Hilom.IntegrationTests.Workforce;

/// <summary>Builds wards, staff, assignments and shifts through the API, for tests about ward-based access.</summary>
internal sealed class StaffFixture(HilomApiFactory factory, HttpClient admin)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Today in hospital time, as the API sees it.</summary>
    public static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")).DateTime);

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Marker() => $"R{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant();

    public HttpClient Admin => admin;

    public static async Task<StaffFixture> CreateAsync(HilomApiFactory factory)
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return new StaffFixture(factory, admin);
    }

    public async Task<string> WardAsync()
    {
        var response = await admin.PostAsJsonAsync("/api/facility/wards", new { code = Marker(), name = "Ward", wardType = "general" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await IdAsync(response);
    }

    public async Task<string> ShiftAsync(string starts = "06:00", string ends = "14:00")
    {
        var code = $"S{Guid.NewGuid().ToString("N")[..7]}".ToUpperInvariant();
        var response = await admin.PostAsJsonAsync("/api/workforce/shifts", new { code, name = code, startsAt = starts, endsAt = ends, color = "am" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await IdAsync(response);
    }

    public async Task<string> NurseAsync(string lastName = "Reyes")
    {
        async Task<string> Lookup(string list) =>
            await IdAsync(await admin.PostAsJsonAsync($"/api/workforce/{list}", new { name = $"{list} {Marker()}" }, Ct));

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
        return await IdAsync(response);
    }

    public async Task AssignAsync(string employeeId, string wardId, DateOnly startsOn, DateOnly? lastDay = null)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/workforce/ward-assignments",
            new { employeeId, wardId, startsOn = Day(startsOn), lastDay = lastDay is { } d ? Day(d) : null },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Signs in a user holding the role, linked to the employee (as accepting an invite does).</summary>
    public async Task<HttpClient> SignInAsEmployeeAsync(string employeeId, string role)
    {
        const string password = "Staff-Fixture-Pass-2026";
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

    private static async Task<string> IdAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
}

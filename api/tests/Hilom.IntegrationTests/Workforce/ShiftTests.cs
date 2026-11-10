using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;

namespace Hilom.IntegrationTests.Workforce;

[Collection(ApiTestGroup.Name)]
public sealed class ShiftTests(HilomApiFactory factory)
{
    private const string Url = "/api/workforce/shifts";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Unique shift code (letters and digits, at most 10) so tests sharing the database don't collide.</summary>
    private static string Code() => $"S{Guid.NewGuid().ToString("N")[..7]}".ToUpperInvariant();

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string code, string starts, string ends, int? sortOrder = null)
    {
        var response = await client.PostAsJsonAsync(
            Url,
            new { code, name = $"Shift {code}", startsAt = starts, endsAt = ends, color = "am", sortOrder },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement[]> AllAsync(HttpClient client, string query = "") =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"{Url}?pageSize=100{query}", Ct)).GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Night_shifts_cross_midnight()
    {
        var admin = await AdminAsync();

        var day = await CreateAsync(admin, Code().ToLowerInvariant(), "06:00:00", "14:00:00");
        var night = await CreateAsync(admin, Code(), "22:00", "06:00");

        day.GetProperty("code").GetString().ShouldBe(day.GetProperty("code").GetString()!.ToUpperInvariant());
        day.GetProperty("crossesMidnight").GetBoolean().ShouldBeFalse();
        day.GetProperty("durationMinutes").GetInt32().ShouldBe(480);
        night.GetProperty("startsAt").GetString().ShouldBe("22:00:00");
        night.GetProperty("crossesMidnight").GetBoolean().ShouldBeTrue();
        night.GetProperty("durationMinutes").GetInt32().ShouldBe(480);
    }

    [Fact]
    public async Task Shifts_are_listed_in_roster_order()
    {
        var admin = await AdminAsync();
        var late = await CreateAsync(admin, Code(), "14:00", "22:00", sortOrder: 902);
        var early = await CreateAsync(admin, Code(), "06:00", "14:00", sortOrder: 901);

        var ids = (await AllAsync(admin)).Select(s => s.GetProperty("id").GetString()).ToList();

        ids.IndexOf(early.GetProperty("id").GetString()).ShouldBeLessThan(ids.IndexOf(late.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Codes_are_unique_and_a_shift_needs_a_length()
    {
        var admin = await AdminAsync();
        var code = Code();
        await CreateAsync(admin, code, "07:00", "15:00");

        var duplicate = await admin.PostAsJsonAsync(Url, new { code = code.ToLowerInvariant(), name = "Copy", startsAt = "07:00", endsAt = "19:00", color = "pm" }, Ct);
        var zero = await admin.PostAsJsonAsync(Url, new { code = Code(), name = "Zero", startsAt = "07:00", endsAt = "07:00", color = "pm" }, Ct);
        var missing = await admin.PostAsJsonAsync(Url, new { code = Code(), name = "No times", color = "noc" }, Ct);

        (await duplicate.ProblemCodeAsync()).ShouldBe("shift_code_taken");
        (await zero.ReadJsonAsync()).GetProperty("errors").GetProperty("endsAt")[0].GetString()
            .ShouldBe("A shift can't start and end at the same time.");
        var errors = (await missing.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("startsAt")[0].GetString().ShouldBe("Enter the start time.");
        errors.GetProperty("endsAt")[0].GetString().ShouldBe("Enter the end time.");
    }

    [Fact]
    public async Task Shifts_are_edited_with_a_version_and_deactivated_not_deleted()
    {
        var admin = await AdminAsync();
        var shift = await CreateAsync(admin, Code(), "07:00", "15:00");
        var url = $"{Url}/{shift.GetProperty("id").GetString()}";
        var version = shift.GetProperty("version").GetUInt32();
        var body = new { code = shift.GetProperty("code").GetString(), name = "Day (12 h)", startsAt = "07:00", endsAt = "19:00", color = "pm", sortOrder = 5, version };

        var saved = await admin.PutAsJsonAsync(url, body, Ct);
        var stale = await admin.PutAsJsonAsync(url, body, Ct);
        (await saved.ReadJsonAsync()).GetProperty("durationMinutes").GetInt32().ShouldBe(720);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");

        (await admin.PostAsync($"{url}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AllAsync(admin, "&status=active")).ShouldNotContain(s => s.GetProperty("id").GetString() == shift.GetProperty("id").GetString());
        (await AllAsync(admin, "&status=inactive")).ShouldContain(s => s.GetProperty("id").GetString() == shift.GetProperty("id").GetString());
        (await admin.PostAsync($"{Url}/{Guid.CreateVersion7()}/activate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Everyone_reads_shifts_but_only_shift_managers_change_them()
    {
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var (chief, _) = await factory.SignInWithRolesAsync("Chief Nurse");

        (await nurse.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await nurse.PostAsJsonAsync(Url, new { code = Code(), name = "X", startsAt = "07:00", endsAt = "15:00", color = "am" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await CreateAsync(chief, Code(), "07:00", "15:00");
    }
}

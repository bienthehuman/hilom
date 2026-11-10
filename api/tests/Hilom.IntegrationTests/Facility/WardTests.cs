using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Facility;

[Collection(ApiTestGroup.Name)]
public sealed class WardTests(HilomApiFactory factory)
{
    private const string Url = "/api/facility/wards";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Unique, code-safe prefix so tests sharing the database don't collide.</summary>
    private static string Marker() => $"T{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant();

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    private static async Task<JsonElement> CreateWardAsync(HttpClient client, string code, string? departmentId = null)
    {
        var response = await client.PostAsJsonAsync(
            Url,
            new { code, name = $"Ward {code}", wardType = "general", departmentId },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement> AddBedAsync(HttpClient client, string wardId, string label)
    {
        var response = await client.PostAsJsonAsync($"{Url}/{wardId}/beds", new { label }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query) =>
        await client.GetFromJsonAsync<JsonElement>($"{Url}?{query}", Ct);

    [Fact]
    public async Task Capacity_is_the_number_of_active_beds()
    {
        var admin = await AdminAsync();
        var code = $"{Marker()}-3";
        var ward = await CreateWardAsync(admin, code.ToLowerInvariant());
        var wardId = ward.GetProperty("id").GetString()!;
        ward.GetProperty("code").GetString().ShouldBe(code);
        ward.GetProperty("capacity").GetInt32().ShouldBe(0);

        await AddBedAsync(admin, wardId, "Bed 10");
        await AddBedAsync(admin, wardId, "Bed 2");
        var broken = await AddBedAsync(admin, wardId, "Bed 1");
        (await admin.PostAsync($"/api/facility/beds/{broken.GetProperty("id").GetString()}/deactivate", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await admin.GetFromJsonAsync<JsonElement>($"{Url}/{wardId}", Ct);
        detail.GetProperty("capacity").GetInt32().ShouldBe(2);
        detail.GetProperty("beds").EnumerateArray().Select(b => b.GetProperty("label").GetString())
            .ShouldBe(["Bed 1", "Bed 2", "Bed 10"]);

        var listed = (await ListAsync(admin, $"q={code}")).GetProperty("items")[0];
        listed.GetProperty("capacity").GetInt32().ShouldBe(2);
        listed.GetProperty("bedCount").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task A_deactivated_ward_has_no_capacity_and_is_filtered_out()
    {
        var admin = await AdminAsync();
        var code = $"{Marker()}-1";
        var ward = await CreateWardAsync(admin, code);
        var wardId = ward.GetProperty("id").GetString()!;
        await AddBedAsync(admin, wardId, "1");

        (await admin.PostAsync($"{Url}/{wardId}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(admin, $"q={code}&status=active")).GetProperty("total").GetInt32().ShouldBe(0);
        var inactive = (await ListAsync(admin, $"q={code}&status=inactive")).GetProperty("items")[0];
        inactive.GetProperty("capacity").GetInt32().ShouldBe(0);
        inactive.GetProperty("bedCount").GetInt32().ShouldBe(1);

        var id = Guid.Parse(wardId);
        var events = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.Event == AuditEvents.Facility.WardChanged.Key));
        events.Count.ShouldBe(2);
        events[0].Details.ShouldNotBeNull().ShouldContain("deactivated");
    }

    [Fact]
    public async Task Wards_are_listed_in_natural_code_order()
    {
        var admin = await AdminAsync();
        var marker = Marker();
        foreach (var number in new[] { "10", "2", "1" })
        {
            await CreateWardAsync(admin, $"{marker}-{number}");
        }

        var codes = (await ListAsync(admin, $"q={marker}")).GetProperty("items").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString());

        codes.ShouldBe([$"{marker}-1", $"{marker}-2", $"{marker}-10"]);
    }

    [Fact]
    public async Task Ward_codes_and_bed_labels_are_unique_ignoring_case()
    {
        var admin = await AdminAsync();
        var code = Marker();
        var first = await CreateWardAsync(admin, code);
        var second = await CreateWardAsync(admin, $"{code}-B");
        await AddBedAsync(admin, first.GetProperty("id").GetString()!, "3A");

        var duplicateWard = await admin.PostAsJsonAsync(Url, new { code = code.ToLowerInvariant(), name = "Copy", wardType = "icu" }, Ct);
        var duplicateBed = await admin.PostAsJsonAsync($"{Url}/{first.GetProperty("id").GetString()}/beds", new { label = "3a" }, Ct);

        (await duplicateWard.ProblemCodeAsync()).ShouldBe("ward_code_taken");
        (await duplicateBed.ProblemCodeAsync()).ShouldBe("bed_label_taken");
        await AddBedAsync(admin, second.GetProperty("id").GetString()!, "3A");
    }

    [Fact]
    public async Task Wards_can_belong_to_an_active_department()
    {
        var admin = await AdminAsync();
        var department = await (await admin.PostAsJsonAsync("/api/workforce/departments", new { name = $"Medicine {Marker()}" }, Ct)).ReadJsonAsync();
        var departmentId = department.GetProperty("id").GetString()!;

        var ward = await CreateWardAsync(admin, Marker(), departmentId);
        ward.GetProperty("department").GetProperty("name").GetString().ShouldBe(department.GetProperty("name").GetString());

        (await admin.PostAsync($"/api/workforce/departments/{departmentId}/deactivate", null, Ct)).EnsureSuccessStatusCode();
        var rejected = await admin.PostAsJsonAsync(Url, new { code = Marker(), name = "New", wardType = "general", departmentId }, Ct);
        var unknown = await admin.PostAsJsonAsync(Url, new { code = Marker(), name = "New", wardType = "general", departmentId = Guid.CreateVersion7() }, Ct);
        var kept = await admin.PutAsJsonAsync(
            $"{Url}/{ward.GetProperty("id").GetString()}",
            new { code = ward.GetProperty("code").GetString(), name = "Renamed", wardType = "surgical", departmentId, version = ward.GetProperty("version").GetUInt32() },
            Ct);

        (await rejected.ReadJsonAsync()).GetProperty("errors").GetProperty("departmentId")[0].GetString().ShouldBe("Choose a department from the list.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await kept.ReadJsonAsync()).GetProperty("department").GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Stale_versions_and_unknown_ids_are_rejected()
    {
        var admin = await AdminAsync();
        var ward = await CreateWardAsync(admin, Marker());
        var bed = await AddBedAsync(admin, ward.GetProperty("id").GetString()!, "1");
        var bedUrl = $"/api/facility/beds/{bed.GetProperty("id").GetString()}";
        var version = bed.GetProperty("version").GetUInt32();

        (await admin.PutAsJsonAsync(bedUrl, new { label = "1A", version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await admin.PutAsJsonAsync(bedUrl, new { label = "1B", version }, Ct);
        var missingWard = await admin.GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct);
        var missingBed = await admin.PostAsync($"/api/facility/beds/{Guid.CreateVersion7()}/deactivate", null, Ct);

        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
        (await missingWard.ProblemCodeAsync()).ShouldBe("ward_not_found");
        (await missingBed.ProblemCodeAsync()).ShouldBe("bed_not_found");
    }

    [Fact]
    public async Task Staff_can_see_wards_but_only_managers_change_them()
    {
        var admin = await AdminAsync();
        var ward = await CreateWardAsync(admin, Marker());
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var (chief, _) = await factory.SignInWithRolesAsync("Chief Nurse");
        var (nobody, _) = await factory.SignInWithRolesAsync();

        (await nurse.GetAsync($"{Url}/{ward.GetProperty("id").GetString()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await nurse.PostAsJsonAsync($"{Url}/{ward.GetProperty("id").GetString()}/beds", new { label = "9" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AddBedAsync(chief, ward.GetProperty("id").GetString()!, "9");
    }
}

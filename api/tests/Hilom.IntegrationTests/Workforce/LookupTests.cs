using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;

namespace Hilom.IntegrationTests.Workforce;

/// <summary>Departments, positions and specializations share one set of endpoints and rules.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class LookupTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Lookups => ["departments", "positions", "specializations"];

    private static string Url(string lookup) => $"/api/workforce/{lookup}";

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string lookup, string name)
    {
        var response = await client.PostAsJsonAsync(Url(lookup), new { name }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadJsonAsync();
    }

    [Theory]
    [MemberData(nameof(Lookups))]
    public async Task Entries_can_be_created_renamed_deactivated_and_reactivated(string lookup)
    {
        var admin = await AdminAsync();
        var name = Unique("Cardiology");

        var created = await CreateAsync(admin, lookup, $"  {name}  ");
        created.GetProperty("name").GetString().ShouldBe(name);
        created.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        var id = created.GetProperty("id").GetString();

        var renamed = await admin.PutAsJsonAsync(
            $"{Url(lookup)}/{id}",
            new { name = name + " Dept", version = created.GetProperty("version").GetUInt32() },
            Ct);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await renamed.ReadJsonAsync()).GetProperty("name").GetString().ShouldBe(name + " Dept");

        (await admin.PostAsync($"{Url(lookup)}/{id}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var inactive = await admin.GetFromJsonAsync<JsonElement>($"{Url(lookup)}?q={Uri.EscapeDataString(name)}&status=inactive", Ct);
        inactive.GetProperty("items")[0].GetProperty("isActive").GetBoolean().ShouldBeFalse();
        var active = await admin.GetFromJsonAsync<JsonElement>($"{Url(lookup)}?q={Uri.EscapeDataString(name)}&status=active", Ct);
        active.GetProperty("total").GetInt32().ShouldBe(0);

        (await admin.PostAsync($"{Url(lookup)}/{id}/activate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        active = await admin.GetFromJsonAsync<JsonElement>($"{Url(lookup)}?q={Uri.EscapeDataString(name)}&status=active", Ct);
        active.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Lists_are_searched_sorted_and_paged()
    {
        var admin = await AdminAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        foreach (var name in new[] { "Bravo", "Alpha", "Charlie" })
        {
            await CreateAsync(admin, "positions", $"{name} {marker}");
        }

        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/workforce/positions?q={marker}&pageSize=2&sort=-name", Ct);

        page.GetProperty("total").GetInt32().ShouldBe(3);
        page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .ShouldBe([$"Charlie {marker}", $"Bravo {marker}"]);
    }

    [Theory]
    [MemberData(nameof(Lookups))]
    public async Task Names_are_unique_ignoring_case(string lookup)
    {
        var admin = await AdminAsync();
        var name = Unique("Pediatrics");
        await CreateAsync(admin, lookup, name);
        var other = await CreateAsync(admin, lookup, Unique("Surgery"));

        var duplicate = await admin.PostAsJsonAsync(Url(lookup), new { name = name.ToUpperInvariant() }, Ct);
        var renamedOnto = await admin.PutAsJsonAsync(
            $"{Url(lookup)}/{other.GetProperty("id").GetString()}",
            new { name, version = other.GetProperty("version").GetUInt32() },
            Ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ProblemCodeAsync()).ShouldBe("lookup_name_taken");
        (await renamedOnto.ProblemCodeAsync()).ShouldBe("lookup_name_taken");
    }

    [Fact]
    public async Task Saving_with_a_stale_version_is_rejected()
    {
        var admin = await AdminAsync();
        var created = await CreateAsync(admin, "departments", Unique("Radiology"));
        var url = $"/api/workforce/departments/{created.GetProperty("id").GetString()}";
        var version = created.GetProperty("version").GetUInt32();

        (await admin.PutAsJsonAsync(url, new { name = Unique("Imaging"), version }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await admin.PutAsJsonAsync(url, new { name = Unique("Diagnostics"), version }, Ct);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Blank_names_and_unknown_ids_are_rejected()
    {
        var admin = await AdminAsync();

        var blank = await admin.PostAsJsonAsync("/api/workforce/specializations", new { name = "   " }, Ct);
        var missing = await admin.PostAsync($"/api/workforce/specializations/{Guid.CreateVersion7()}/deactivate", null, Ct);

        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await blank.ReadJsonAsync()).GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("Enter a name.");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.ProblemCodeAsync()).ShouldBe("lookup_not_found");
    }

    [Fact]
    public async Task Any_signed_in_user_can_read_lookups_but_only_managers_can_change_them()
    {
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var anonymous = factory.CreateBrowserClient();
        await anonymous.PrimeAntiforgeryAsync();

        (await nurse.GetAsync("/api/workforce/departments", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/workforce/departments", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await nurse.PostAsJsonAsync("/api/workforce/departments", new { name = Unique("Oncology") }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await CreateAsync(hr, "departments", Unique("Oncology"));
    }

    [Fact]
    public async Task Changes_are_recorded_as_data_changes()
    {
        var admin = await AdminAsync();
        var created = await CreateAsync(admin, "departments", Unique("Neurology"));
        var id = Guid.Parse(created.GetProperty("id").GetString()!);

        (await admin.PostAsync($"/api/workforce/departments/{id}/deactivate", null, Ct)).EnsureSuccessStatusCode();

        var entries = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id));
        entries.Select(e => e.EntityType).ShouldAllBe(t => t == "Department");
        entries.Count.ShouldBe(2);
        entries[0].Changes.ShouldNotBeNull().ShouldContain("isActive");
    }
}

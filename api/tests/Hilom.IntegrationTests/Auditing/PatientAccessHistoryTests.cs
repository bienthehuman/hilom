using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;

namespace Hilom.IntegrationTests.Auditing;

/// <summary>The patient "Access history" tab and the audit log's patient filter (docs/AUDIT.md).</summary>
[Collection(ApiTestGroup.Name)]
public sealed class PatientAccessHistoryTests(HilomApiFactory factory)
{
    private const string ContactNumber = "0917 555 0142";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string HistoryUrl(Guid patientId) => $"/api/patients/{patientId}/access-history";

    /// <summary>Front desk registers a patient and opens the record: a registration, its data change and a view.</summary>
    private async Task<(Guid PatientId, string FrontDeskName)> RegisteredAndViewedAsync()
    {
        var (frontDesk, user) = await factory.SignInWithRolesAsync("Front Desk");
        var registered = await frontDesk.PostAsJsonAsync(
            "/api/patients",
            new
            {
                firstName = "Lorna",
                lastName = $"Villanueva {Guid.NewGuid().ToString("N")[..8]}",
                birthDate = "1971-06-02",
                sex = "female",
                address = "4 Rizal Ave., Pasig City",
                contactNumber = ContactNumber,
                emergencyContactName = "Ben Villanueva",
                emergencyContactRelation = "Son",
                emergencyContactNumber = "0918 555 0100",
            },
            Ct);
        registered.StatusCode.ShouldBe(HttpStatusCode.Created, await registered.Content.ReadAsStringAsync(Ct));
        var patientId = (await registered.ReadJsonAsync()).GetProperty("id").GetGuid();

        (await frontDesk.GetAsync($"/api/patients/{patientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (patientId, user.DisplayName);
    }

    [Fact]
    public async Task The_chief_nurse_sees_who_viewed_and_changed_a_patient_without_the_values()
    {
        var (patientId, frontDeskName) = await RegisteredAndViewedAsync();
        var (chiefNurse, chiefNurseUser) = await factory.SignInWithRolesAsync("Chief Nurse");

        var response = await chiefNurse.GetAsync(HistoryUrl(patientId), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        raw.ShouldNotContain(ContactNumber); // field names only; values stay in the full audit log
        var page = JsonDocument.Parse(raw).RootElement;
        var items = page.GetProperty("items").EnumerateArray().ToList();
        page.GetProperty("total").GetInt32().ShouldBe(items.Count);

        var viewed = items.Where(i => i.GetProperty("event").GetString() == "patients.record.viewed").ShouldHaveSingleItem();
        viewed.GetProperty("kind").GetString().ShouldBe("read");
        viewed.GetProperty("label").GetString().ShouldNotBeNullOrWhiteSpace();
        viewed.GetProperty("userName").GetString().ShouldBe(frontDeskName);
        viewed.GetProperty("viaPortal").GetBoolean().ShouldBeFalse();

        items.ShouldContain(i => i.GetProperty("event").GetString() == "patients.patient.registered");
        var change = items.First(i => i.GetProperty("kind").GetString() == "data_change" && i.GetProperty("entityType").GetString() == "Patient");
        change.GetProperty("operation").GetString().ShouldBe("added");
        change.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()).ShouldContain("contactNumber");

        // Newest first: the view came after the registration.
        items[0].GetProperty("event").GetString().ShouldBe("patients.record.viewed");

        // Reading the history is reading the audit log, and isn't added to the history itself.
        var recorded = (await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.audit.viewed" && e.UserId == chiefNurseUser.Id))).ShouldHaveSingleItem();
        recorded.PatientId.ShouldBeNull();
        recorded.Details!.ShouldContain(patientId.ToString());
        recorded.Details!.ShouldContain("patient_access_history");
    }

    [Fact]
    public async Task The_history_filters_by_kind()
    {
        var (patientId, _) = await RegisteredAndViewedAsync();
        var (chiefNurse, _) = await factory.SignInWithRolesAsync("Chief Nurse");

        var reads = await chiefNurse.GetFromJsonAsync<JsonElement>($"{HistoryUrl(patientId)}?kind=read", Ct);

        reads.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("kind").GetString().ShouldBe("read");
    }

    [Fact]
    public async Task Without_the_permission_the_history_is_forbidden_and_an_unknown_patient_is_not_found()
    {
        var (patientId, _) = await RegisteredAndViewedAsync();
        var (staffNurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var (frontDesk, _) = await factory.SignInWithRolesAsync("Front Desk");
        var (chiefNurse, _) = await factory.SignInWithRolesAsync("Chief Nurse");

        (await staffNurse.GetAsync(HistoryUrl(patientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await frontDesk.GetAsync(HistoryUrl(patientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var missing = await chiefNurse.GetAsync(HistoryUrl(Guid.NewGuid()), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.ProblemCodeAsync()).ShouldBe("patient_not_found");
    }

    [Fact]
    public async Task The_audit_log_filters_by_patient()
    {
        var (patientId, _) = await RegisteredAndViewedAsync();
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/access/audit?patientId={patientId}", Ct);

        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBeGreaterThanOrEqualTo(3);
        items.ShouldAllBe(i => i.GetProperty("patientId").GetGuid() == patientId);
    }
}

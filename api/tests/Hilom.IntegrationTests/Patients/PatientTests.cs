using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Patients;

[Collection(ApiTestGroup.Name)]
public sealed partial class PatientTests(HilomApiFactory factory)
{
    private const string Url = "/api/patients";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A last name no other test uses, so searches only find this test's patients.</summary>
    private static string UniqueLastName() => $"Dela Cruz {Guid.NewGuid().ToString("N")[..8]}";

    private static Dictionary<string, object?> Juan(string lastName, string birthDate = "1958-03-21") => new()
    {
        ["firstName"] = "Juan",
        ["middleName"] = "Santos",
        ["lastName"] = lastName,
        ["birthDate"] = birthDate,
        ["sex"] = "male",
        ["civilStatus"] = "married",
        ["religion"] = "Roman Catholic",
        ["address"] = "12 Mabini St., Brgy. San Roque, Marikina City",
        ["contactNumber"] = "0917 555 0199",
        ["emergencyContactName"] = "Maria Dela Cruz",
        ["emergencyContactRelation"] = "Daughter",
        ["emergencyContactNumber"] = "0918 555 0123",
    };

    private async Task<HttpClient> FrontDeskAsync() => (await factory.SignInWithRolesAsync("Front Desk")).Client;

    private static async Task<JsonElement> RegisterAsync(HttpClient client, Dictionary<string, object?> body)
    {
        var response = await client.PostAsJsonAsync(Url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static Guid IdOf(JsonElement patient) => Guid.Parse(patient.GetProperty("id").GetString()!);

    [Fact]
    public async Task Front_desk_registers_a_patient_with_a_generated_mrn()
    {
        var (frontDesk, user) = await factory.SignInWithRolesAsync("Front Desk");

        var created = await RegisterAsync(frontDesk, Juan(UniqueLastName()));

        MedicalRecordNumber().IsMatch(created.GetProperty("mrn").GetString()!).ShouldBeTrue();
        created.GetProperty("civilStatus").GetString().ShouldBe("married");
        created.GetProperty("registeredBy").GetString().ShouldBe(user.DisplayName);

        var id = IdOf(created);
        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == id && e.Event == AuditEvents.Patients.PatientRegistered.Key));
        audit.Single().Severity.ShouldBe(AuditSeverity.Notice);
    }

    [Fact]
    public async Task Medical_record_numbers_are_unique_and_increasing()
    {
        var frontDesk = await FrontDeskAsync();

        var first = await RegisterAsync(frontDesk, Juan(UniqueLastName()));
        var second = await RegisterAsync(frontDesk, Juan(UniqueLastName()));

        string.CompareOrdinal(first.GetProperty("mrn").GetString(), second.GetProperty("mrn").GetString()).ShouldBeLessThan(0);
    }

    [Fact]
    public async Task Required_details_and_a_past_birth_date_are_checked()
    {
        var frontDesk = await FrontDeskAsync();
        var body = Juan(UniqueLastName(), birthDate: "2999-01-01");
        body["emergencyContactName"] = " ";
        body["sex"] = null;

        var response = await frontDesk.PostAsJsonAsync(Url, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("birthDate")[0].GetString().ShouldBe("The date of birth can't be in the future.");
        errors.GetProperty("emergencyContactName")[0].GetString().ShouldBe("Enter who to call in an emergency.");
        errors.GetProperty("sex")[0].GetString().ShouldBe("Choose one.");
    }

    [Fact]
    public async Task Search_finds_patients_by_name_mrn_or_birth_date()
    {
        var frontDesk = await FrontDeskAsync();
        var lastName = UniqueLastName();
        var juan = await RegisterAsync(frontDesk, Juan(lastName));
        var pedro = Juan(lastName, birthDate: "1990-07-04");
        pedro["firstName"] = "Pedro";
        await RegisterAsync(frontDesk, pedro);

        async Task<string[]> Search(string query)
        {
            var page = await frontDesk.GetFromJsonAsync<JsonElement>($"{Url}?{query}", Ct);
            return [.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("firstName").GetString()!)];
        }

        var mrn = juan.GetProperty("mrn").GetString()!;
        (await Search($"q={Uri.EscapeDataString(lastName)}")).ShouldBe(["Juan", "Pedro"]);
        (await Search($"q={Uri.EscapeDataString($"{lastName}, Pe")}")).ShouldBe(["Pedro"]);
        (await Search($"q={Uri.EscapeDataString($"Juan Santos {lastName}")}")).ShouldBe(["Juan"]);
        (await Search($"q={mrn}")).ShouldBe(["Juan"]);
        (await Search($"q={Uri.EscapeDataString(lastName)}&birthDate=1990-07-04")).ShouldBe(["Pedro"]);
    }

    [Fact]
    public async Task Staff_without_the_permission_get_403()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var created = await RegisterAsync(await FrontDeskAsync(), Juan(UniqueLastName()));

        (await hr.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await hr.GetAsync($"{Url}/{IdOf(created)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await hr.PostAsJsonAsync(Url, Juan(UniqueLastName()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_ward_nurse_reaches_no_patients_until_they_are_admitted_to_the_ward()
    {
        // patients.view at `ward` scope reaches patients in a bed on the nurse's ward. Admissions come in phase 3,
        // so for now a ward-scoped nurse sees nobody, and opening a record is denied and logged.
        var (nurse, user) = await factory.SignInWithRolesAsync("Staff Nurse");
        var created = await RegisterAsync(await FrontDeskAsync(), Juan(UniqueLastName()));
        var id = IdOf(created);

        var list = await nurse.GetFromJsonAsync<JsonElement>(Url, Ct);
        var open = await nurse.GetAsync($"{Url}/{id}", Ct);

        list.GetProperty("total").GetInt32().ShouldBe(0);
        open.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await open.ProblemCodeAsync()).ShouldBe("patient_not_found");
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.UserId == user.Id && e.Event == AuditEvents.Access.Denied.Key));
        denied.Single().Details.ShouldNotBeNull().ShouldContain("out_of_scope");
    }

    [Fact]
    public async Task A_head_nurse_cannot_edit_a_patient_outside_their_ward()
    {
        var (headNurse, _) = await factory.SignInWithRolesAsync("Head Nurse");
        var created = await RegisterAsync(await FrontDeskAsync(), Juan(UniqueLastName()));
        var body = Juan(UniqueLastName());
        body["version"] = created.GetProperty("version").GetUInt32();

        var response = await headNurse.PutAsJsonAsync($"{Url}/{IdOf(created)}", body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Opening_a_record_is_logged_once_per_user_per_15_minutes()
    {
        var frontDesk = await FrontDeskAsync();
        var (chiefNurse, chief) = await factory.SignInWithRolesAsync("Chief Nurse");
        var created = await RegisterAsync(frontDesk, Juan(UniqueLastName()));
        var id = IdOf(created);

        for (var i = 0; i < 3; i++)
        {
            (await chiefNurse.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await frontDesk.GetAsync($"{Url}/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var views = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == id && e.Event == AuditEvents.Patients.RecordViewed.Key));
        views.Count.ShouldBe(2);
        var chiefView = views.Single(v => v.UserId == chief.Id);
        chiefView.Kind.ShouldBe(AuditKind.Read);
        chiefView.EntityId.ShouldBe(id);
    }

    [Fact]
    public async Task Editing_demographics_records_which_fields_changed()
    {
        var frontDesk = await FrontDeskAsync();
        var created = await RegisterAsync(frontDesk, Juan(UniqueLastName()));
        var id = IdOf(created);
        var body = Juan(created.GetProperty("lastName").GetString()!);
        body["contactNumber"] = "0919 555 0000";
        body["religion"] = "  ";
        body["version"] = created.GetProperty("version").GetUInt32();

        var response = await frontDesk.PutAsJsonAsync($"{Url}/{id}", body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var saved = await response.ReadJsonAsync();
        saved.GetProperty("contactNumber").GetString().ShouldBe("0919 555 0000");
        saved.GetProperty("religion").ValueKind.ShouldBe(JsonValueKind.Null);

        var events = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == id && e.Event == AuditEvents.Patients.DemographicsChanged.Key));
        var details = JsonDocument.Parse(events.Single().Details!).RootElement;
        details.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ShouldBe(["religion", "contactNumber"]);

        var changes = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == id && e.Kind == AuditKind.DataChange && e.CorrelationId == events.Single().CorrelationId));
        changes.Single().Changes.ShouldNotBeNull().ShouldContain("0919 555 0000");
    }

    [Fact]
    public async Task Saving_without_changes_records_no_event()
    {
        var frontDesk = await FrontDeskAsync();
        var created = await RegisterAsync(frontDesk, Juan(UniqueLastName()));
        var id = IdOf(created);
        var body = Juan(created.GetProperty("lastName").GetString()!);
        body["version"] = created.GetProperty("version").GetUInt32();

        (await frontDesk.PutAsJsonAsync($"{Url}/{id}", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var events = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == id && e.Event == AuditEvents.Patients.DemographicsChanged.Key));
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_with_a_stale_version_is_rejected()
    {
        var frontDesk = await FrontDeskAsync();
        var created = await RegisterAsync(frontDesk, Juan(UniqueLastName()));
        var url = $"{Url}/{IdOf(created)}";
        var body = Juan(UniqueLastName());
        body["version"] = created.GetProperty("version").GetUInt32();

        (await frontDesk.PutAsJsonAsync(url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await frontDesk.PutAsJsonAsync(url, body, Ct);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task An_unknown_patient_is_404()
    {
        var frontDesk = await FrontDeskAsync();

        var response = await frontDesk.GetAsync($"{Url}/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ProblemCodeAsync()).ShouldBe("patient_not_found");
    }

    [GeneratedRegex(@"^MRN-\d{6,}$")]
    private static partial Regex MedicalRecordNumber();
}

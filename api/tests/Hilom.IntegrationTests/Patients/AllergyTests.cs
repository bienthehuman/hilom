using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Patients;

[Collection(ApiTestGroup.Name)]
public sealed class AllergyTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Penicillin(string severity = "severe") =>
        new { allergen = "Penicillin", category = "drug", severity, reaction = "Anaphylaxis" };

    private async Task<HttpClient> ChiefNurseAsync() => (await factory.SignInWithRolesAsync("Chief Nurse")).Client;

    /// <summary>A newly registered patient; returns the allergies URL.</summary>
    private async Task<(Guid PatientId, string Url)> PatientAsync()
    {
        var frontDesk = (await factory.SignInWithRolesAsync("Front Desk")).Client;
        var response = await frontDesk.PostAsJsonAsync(
            "/api/patients",
            new
            {
                firstName = "Juan",
                lastName = $"Allergy {Guid.NewGuid().ToString("N")[..8]}",
                birthDate = "1958-03-21",
                sex = "male",
                address = "12 Mabini St., Marikina City",
                contactNumber = "0917 555 0199",
                emergencyContactName = "Maria Dela Cruz",
                emergencyContactRelation = "Daughter",
                emergencyContactNumber = "0918 555 0123",
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = Guid.Parse((await response.ReadJsonAsync()).GetProperty("id").GetString()!);
        return (id, $"/api/patients/{id}/allergies");
    }

    private static async Task<JsonElement> RecordAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static string ItemUrl(string url, JsonElement allergy) => $"{url}/{allergy.GetProperty("id").GetString()}";

    private static uint VersionOf(JsonElement allergy) => allergy.GetProperty("version").GetUInt32();

    [Fact]
    public async Task A_nurse_records_an_allergy_and_it_is_audited_for_the_patient()
    {
        var (nurse, user) = await factory.SignInWithRolesAsync("Chief Nurse");
        var (patientId, url) = await PatientAsync();

        var created = await RecordAsync(nurse, url, Penicillin());

        created.GetProperty("status").GetString().ShouldBe("active");
        created.GetProperty("reaction").GetString().ShouldBe("Anaphylaxis");
        created.GetProperty("recordedBy").GetString().ShouldBe(user.DisplayName);

        var recorded = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == patientId && e.Event == AuditEvents.Patients.AllergyRecorded.Key));
        recorded.Single().Details.ShouldNotBeNull().ShouldContain("Penicillin");
        var dataChanges = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == patientId && e.Kind == AuditKind.DataChange && e.EntityType == "PatientAllergy"));
        dataChanges.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_allergen_is_active_at_most_once_ignoring_case()
    {
        var nurse = await ChiefNurseAsync();
        var (_, url) = await PatientAsync();
        var first = await RecordAsync(nurse, url, Penicillin());

        var duplicate = await nurse.PostAsJsonAsync(url, new { allergen = " penicillin ", category = "drug", severity = "mild" }, Ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ProblemCodeAsync()).ShouldBe("allergy_already_recorded");

        // Once resolved, it can be recorded again.
        var resolve = await nurse.PostAsJsonAsync($"{ItemUrl(url, first)}/status", new { status = "resolved", version = VersionOf(first) }, Ct);
        resolve.StatusCode.ShouldBe(HttpStatusCode.OK);
        await RecordAsync(nurse, url, Penicillin("moderate"));
    }

    [Fact]
    public async Task The_database_also_refuses_two_active_entries_for_one_allergen()
    {
        var nurse = await ChiefNurseAsync();
        var (patientId, url) = await PatientAsync();
        await RecordAsync(nurse, url, Penicillin());

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            insert into patients.patient_allergies (id, patient_id, allergen, category, severity, status, created_at, updated_at)
            values (gen_random_uuid(), @patient, 'PENICILLIN', 'drug', 'mild', 'active', now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("patient", patientId);

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("ux_patient_allergies_active_allergen");
    }

    [Fact]
    public async Task Required_details_are_checked()
    {
        var nurse = await ChiefNurseAsync();
        var (_, url) = await PatientAsync();

        var response = await nurse.PostAsJsonAsync(url, new { allergen = " ", category = (string?)null, severity = "severe" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("allergen")[0].GetString().ShouldBe("Enter what the patient is allergic to.");
        errors.GetProperty("category")[0].GetString().ShouldBe("Choose a category.");
    }

    [Fact]
    public async Task Editing_records_the_changed_fields_and_checks_the_version()
    {
        var nurse = await ChiefNurseAsync();
        var (patientId, url) = await PatientAsync();
        var created = await RecordAsync(nurse, url, Penicillin("moderate"));
        var body = new { allergen = "Penicillin", category = "drug", severity = "severe", reaction = "Anaphylaxis", version = VersionOf(created) };

        var edited = await nurse.PutAsJsonAsync(ItemUrl(url, created), body, Ct);
        var stale = await nurse.PutAsJsonAsync(ItemUrl(url, created), body, Ct);

        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await edited.ReadJsonAsync()).GetProperty("severity").GetString().ShouldBe("severe");
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
        var changed = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == patientId && e.Event == AuditEvents.Patients.AllergyChanged.Key));
        JsonDocument.Parse(changed.Single().Details!).RootElement.GetProperty("fields")[0].GetString().ShouldBe("severity");
    }

    [Fact]
    public async Task Entered_in_error_needs_a_reason_and_ends_the_entry()
    {
        var nurse = await ChiefNurseAsync();
        var (_, url) = await PatientAsync();
        var created = await RecordAsync(nurse, url, Penicillin());
        var statusUrl = $"{ItemUrl(url, created)}/status";

        var withoutReason = await nurse.PostAsJsonAsync(statusUrl, new { status = "entered_in_error", version = VersionOf(created) }, Ct);
        var withReason = await nurse.PostAsJsonAsync(statusUrl, new { status = "entered_in_error", note = "Wrong patient", version = VersionOf(created) }, Ct);

        withoutReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await withoutReason.ReadJsonAsync()).GetProperty("errors").GetProperty("note")[0].GetString()
            .ShouldBe("Say what was wrong with this entry.");
        withReason.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ended = await withReason.ReadJsonAsync();
        ended.GetProperty("status").GetString().ShouldBe("entered_in_error");
        ended.GetProperty("statusNote").GetString().ShouldBe("Wrong patient");

        var edit = await nurse.PutAsJsonAsync(
            ItemUrl(url, created),
            new { allergen = "Penicillin", category = "drug", severity = "mild", version = VersionOf(ended) },
            Ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await edit.ProblemCodeAsync()).ShouldBe("allergy_not_active");
    }

    [Fact]
    public async Task The_list_shows_active_entries_most_severe_first_then_history()
    {
        var nurse = await ChiefNurseAsync();
        var (_, url) = await PatientAsync();
        await RecordAsync(nurse, url, new { allergen = "Dust mites", category = "environmental", severity = "mild" });
        var shrimp = await RecordAsync(nurse, url, new { allergen = "Shrimp", category = "food", severity = "moderate", reaction = "Hives" });
        await RecordAsync(nurse, url, Penicillin());
        await RecordAsync(nurse, url, new { allergen = "Latex", category = "other", severity = "moderate" });
        (await nurse.PostAsJsonAsync($"{ItemUrl(url, shrimp)}/status", new { status = "resolved", note = "Outgrown", version = VersionOf(shrimp) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await nurse.GetFromJsonAsync<JsonElement>(url, Ct);

        list.EnumerateArray().Select(a => a.GetProperty("allergen").GetString())
            .ShouldBe(["Penicillin", "Latex", "Dust mites", "Shrimp"]);
    }

    [Fact]
    public async Task Viewing_allergies_counts_as_opening_the_record()
    {
        var (nurse, user) = await factory.SignInWithRolesAsync("Chief Nurse");
        var (patientId, url) = await PatientAsync();

        (await nurse.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await nurse.GetAsync($"/api/patients/{patientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var views = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == patientId && e.UserId == user.Id && e.Event == AuditEvents.Patients.RecordViewed.Key));
        views.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Staff_without_the_permission_get_403_and_out_of_scope_staff_get_404()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var (nurse, nurseUser) = await factory.SignInWithRolesAsync("Staff Nurse");
        var (patientId, url) = await PatientAsync();

        (await hr.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await hr.PostAsJsonAsync(url, Penicillin(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A ward nurse reaches patients on their ward, and nobody is admitted yet (phase 3).
        var record = await nurse.PostAsJsonAsync(url, Penicillin(), Ct);
        record.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await record.ProblemCodeAsync()).ShouldBe("patient_not_found");
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == patientId && e.UserId == nurseUser.Id && e.Event == AuditEvents.Access.Denied.Key));
        denied.Single().Details.ShouldNotBeNull().ShouldContain("patients.allergies.manage");
    }

    [Fact]
    public async Task An_allergy_is_only_reached_through_its_own_patient()
    {
        var nurse = await ChiefNurseAsync();
        var (_, url) = await PatientAsync();
        var (_, otherUrl) = await PatientAsync();
        var created = await RecordAsync(nurse, url, Penicillin());

        var response = await nurse.PostAsJsonAsync(
            $"{otherUrl}/{created.GetProperty("id").GetString()}/status",
            new { status = "resolved", version = VersionOf(created) },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ProblemCodeAsync()).ShouldBe("allergy_not_found");
    }
}

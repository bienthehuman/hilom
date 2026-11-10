using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Patients;

[Collection(ApiTestGroup.Name)]
public sealed class LinkTests(HilomApiFactory factory)
{
    private const string Links = "/api/patients/links";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A portal account made the real way: self-registration.</summary>
    private async Task<(HttpClient Client, Guid UserId)> PortalUserAsync()
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();
        var response = await client.PostAsJsonAsync(
            "/api/access/register",
            new { displayName = "Maria Dela Cruz", email = $"maria.{Guid.NewGuid():N}@family.test", password = "Portal-Password-2026" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (client, Guid.Parse((await response.ReadJsonAsync()).GetProperty("id").GetString()!));
    }

    private async Task<HttpClient> FrontDeskAsync() => (await factory.SignInWithRolesAsync("Front Desk")).Client;

    private static async Task<JsonElement> RegisterPatientAsync(HttpClient frontDesk, string firstName = "Juan")
    {
        var response = await frontDesk.PostAsJsonAsync(
            "/api/patients",
            new
            {
                firstName,
                lastName = $"Dela Cruz {Guid.NewGuid().ToString("N")[..6]}",
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
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement> RequestAsync(HttpClient portal, string relationship = "child")
    {
        var response = await portal.PostAsJsonAsync(
            Links,
            new { patientName = "Juan Santos Dela Cruz", patientBirthDate = "1958-03-21", patientMrn = "mrn-000123", relationship, note = "He is my father." },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    /// <summary>The staff view of a link, with its version.</summary>
    private static async Task<JsonElement> StaffViewAsync(HttpClient frontDesk, string linkId, string status = "pending")
    {
        var page = await frontDesk.GetFromJsonAsync<JsonElement>($"{Links}?status={status}&pageSize=100", Ct);
        return page.GetProperty("items").EnumerateArray().Single(l => l.GetProperty("id").GetString() == linkId);
    }

    private static async Task<HttpResponseMessage> VerifyAsync(
        HttpClient frontDesk,
        string linkId,
        JsonElement patient,
        string relationship = "child",
        bool canBook = true,
        bool canViewRecords = true)
    {
        var link = await StaffViewAsync(frontDesk, linkId);
        return await frontDesk.PostAsJsonAsync(
            $"{Links}/{linkId}/verify",
            new { patientId = patient.GetProperty("id").GetString(), relationship, canBook, canViewRecords, version = link.GetProperty("version").GetUInt32() },
            Ct);
    }

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    [Fact]
    public async Task A_portal_user_requests_a_link_and_it_waits_in_the_front_desk_queue()
    {
        var (portal, userId) = await PortalUserAsync();
        var frontDesk = await FrontDeskAsync();

        var request = await RequestAsync(portal);

        request.GetProperty("status").GetString().ShouldBe("pending");
        var mine = await portal.GetFromJsonAsync<JsonElement>($"{Links}/mine", Ct);
        mine.EnumerateArray().Single().GetProperty("patient").ValueKind.ShouldBe(JsonValueKind.Null);

        var queued = await StaffViewAsync(frontDesk, IdOf(request));
        queued.GetProperty("requester").GetProperty("id").GetString().ShouldBe(userId.ToString());
        queued.GetProperty("requestedMrn").GetString().ShouldBe("MRN-000123");
        queued.GetProperty("requestNote").GetString().ShouldBe("He is my father.");

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == userId && e.Event == AuditEvents.Patients.LinkRequested.Key));
        audit.Single().Severity.ShouldBe(AuditSeverity.Info);
    }

    [Fact]
    public async Task Verifying_grants_access_immediately_and_revoking_removes_it_immediately()
    {
        var (portal, userId) = await PortalUserAsync();
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var patientId = IdOf(patient);
        var request = await RequestAsync(portal);

        (await portal.GetAsync($"/api/patients/{patientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await VerifyAsync(frontDesk, IdOf(request), patient)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await portal.GetAsync($"/api/patients/{patientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await portal.GetAsync($"/api/patients/{patientId}/allergies", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await portal.GetFromJsonAsync<JsonElement>("/api/patients", Ct);
        list.GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([patientId]);

        // The record view names the link it went through.
        var views = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == userId && e.Event == AuditEvents.Patients.RecordViewed.Key));
        views.Single().ActingForPatientLinkId.ShouldBe(Guid.Parse(IdOf(request)));
        var verified = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(IdOf(request)) && e.Event == AuditEvents.Patients.LinkVerified.Key));
        verified.Single().PatientId.ShouldBe(Guid.Parse(patientId));

        var active = await StaffViewAsync(frontDesk, IdOf(request), "active");
        var revoke = await frontDesk.PostAsJsonAsync(
            $"{Links}/{IdOf(request)}/revoke",
            new { reason = "Patient asked to remove access", version = active.GetProperty("version").GetUInt32() },
            Ct);

        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await portal.GetAsync($"/api/patients/{patientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var mine = await portal.GetFromJsonAsync<JsonElement>($"{Links}/mine", Ct);
        var ended = mine.EnumerateArray().Single();
        ended.GetProperty("status").GetString().ShouldBe("revoked");
        ended.GetProperty("patient").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Without_can_view_records_the_account_sees_the_name_but_not_the_record()
    {
        var (portal, _) = await PortalUserAsync();
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var request = await RequestAsync(portal, "representative");

        (await VerifyAsync(frontDesk, IdOf(request), patient, "representative", canBook: true, canViewRecords: false))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var mine = (await portal.GetFromJsonAsync<JsonElement>($"{Links}/mine", Ct)).EnumerateArray().Single();
        mine.GetProperty("patient").GetProperty("mrn").GetString().ShouldBe(patient.GetProperty("mrn").GetString());
        mine.GetProperty("canBook").GetBoolean().ShouldBeTrue();
        (await portal.GetAsync($"/api/patients/{IdOf(patient)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turning record access on takes effect on the next request.
        var active = await StaffViewAsync(frontDesk, IdOf(request), "active");
        var change = await frontDesk.PutAsJsonAsync(
            $"{Links}/{IdOf(request)}/permissions",
            new { canBook = true, canViewRecords = true, version = active.GetProperty("version").GetUInt32() },
            Ct);

        change.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await portal.GetAsync($"/api/patients/{IdOf(patient)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var changed = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(IdOf(request)) && e.Event == AuditEvents.Patients.LinkPermissionsChanged.Key));
        changed.Single().Severity.ShouldBe(AuditSeverity.Critical);
    }

    [Fact]
    public async Task Only_one_account_can_be_the_patient_themselves()
    {
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var (first, _) = await PortalUserAsync();
        var (second, _) = await PortalUserAsync();
        var firstRequest = await RequestAsync(first, "self");
        var secondRequest = await RequestAsync(second, "self");

        (await VerifyAsync(frontDesk, IdOf(firstRequest), patient, "self")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await VerifyAsync(frontDesk, IdOf(secondRequest), patient, "self");

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("self_link_exists");

        // As a representative instead, it's fine.
        (await VerifyAsync(frontDesk, IdOf(secondRequest), patient, "representative")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_database_also_refuses_a_second_active_self_link()
    {
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var (portal, _) = await PortalUserAsync();
        (await VerifyAsync(frontDesk, IdOf(await RequestAsync(portal, "self")), patient, "self")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (_, otherUser) = await PortalUserAsync();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            insert into patients.patient_account_links
              (id, user_id, patient_id, requested_patient_name, requested_birth_date, relationship, can_book, can_view_records,
               status, requested_at, created_at, updated_at)
            values (gen_random_uuid(), @user, @patient, 'Juan', '1958-03-21', 'self', true, true, 'active', now(), now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("user", otherUser);
        command.Parameters.AddWithValue("patient", Guid.Parse(IdOf(patient)));

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("ux_patient_account_links_active_self");
    }

    [Fact]
    public async Task An_account_is_linked_to_a_patient_at_most_once()
    {
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var (portal, _) = await PortalUserAsync();
        var first = await RequestAsync(portal);
        var duplicate = await RequestAsync(portal);
        (await VerifyAsync(frontDesk, IdOf(first), patient)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await VerifyAsync(frontDesk, IdOf(duplicate), patient);

        (await response.ProblemCodeAsync()).ShouldBe("already_linked");
    }

    [Fact]
    public async Task A_rejected_request_tells_the_requester_why_and_cannot_be_verified_later()
    {
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var (portal, _) = await PortalUserAsync();
        var request = await RequestAsync(portal);
        var queued = await StaffViewAsync(frontDesk, IdOf(request));

        var reject = await frontDesk.PostAsJsonAsync(
            $"{Links}/{IdOf(request)}/reject",
            new { reason = "Please bring a valid ID to the front desk.", version = queued.GetProperty("version").GetUInt32() },
            Ct);

        reject.StatusCode.ShouldBe(HttpStatusCode.OK);
        var mine = (await portal.GetFromJsonAsync<JsonElement>($"{Links}/mine", Ct)).EnumerateArray().Single();
        mine.GetProperty("status").GetString().ShouldBe("rejected");
        mine.GetProperty("reviewNote").GetString().ShouldBe("Please bring a valid ID to the front desk.");

        var rejected = await StaffViewAsync(frontDesk, IdOf(request), "rejected");
        var verify = await frontDesk.PostAsJsonAsync(
            $"{Links}/{IdOf(request)}/verify",
            new { patientId = IdOf(patient), relationship = "child", canBook = true, canViewRecords = true, version = rejected.GetProperty("version").GetUInt32() },
            Ct);
        (await verify.ProblemCodeAsync()).ShouldBe("link_not_pending");
    }

    [Fact]
    public async Task A_portal_account_can_have_five_requests_waiting()
    {
        var (portal, _) = await PortalUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await RequestAsync(portal);
        }

        var sixth = await portal.PostAsJsonAsync(
            Links,
            new { patientName = "Juan Dela Cruz", patientBirthDate = "1958-03-21", relationship = "child" },
            Ct);

        (await sixth.ProblemCodeAsync()).ShouldBe("too_many_pending_links");
    }

    [Fact]
    public async Task Requests_and_revocations_are_validated()
    {
        var (portal, _) = await PortalUserAsync();
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);

        var request = await portal.PostAsJsonAsync(Links, new { patientName = " ", patientBirthDate = (string?)null, relationship = (string?)null }, Ct);
        var link = await RequestAsync(portal);
        (await VerifyAsync(frontDesk, IdOf(link), patient)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var active = await StaffViewAsync(frontDesk, IdOf(link), "active");
        var revoke = await frontDesk.PostAsJsonAsync($"{Links}/{IdOf(link)}/revoke", new { reason = "", version = active.GetProperty("version").GetUInt32() }, Ct);

        var errors = (await request.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("patientName")[0].GetString().ShouldBe("Enter the patient's full name.");
        errors.GetProperty("patientBirthDate")[0].GetString().ShouldBe("Enter the patient's date of birth.");
        errors.GetProperty("relationship")[0].GetString().ShouldBe("Choose who the patient is to you.");
        (await revoke.ReadJsonAsync()).GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("Say why the link is ending.");
    }

    [Fact]
    public async Task Each_side_only_gets_its_own_actions()
    {
        var (portal, _) = await PortalUserAsync();
        var frontDesk = await FrontDeskAsync();
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var request = await RequestAsync(portal);

        (await portal.GetAsync(Links, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await portal.PostAsJsonAsync($"{Links}/{IdOf(request)}/reject", new { version = 0 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await frontDesk.PostAsJsonAsync(Links, new { patientName = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nurse.GetAsync(Links, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_patients_links_are_listed_on_their_record()
    {
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var (portal, _) = await PortalUserAsync();
        var request = await RequestAsync(portal);
        (await VerifyAsync(frontDesk, IdOf(request), patient, canViewRecords: false)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var links = await frontDesk.GetFromJsonAsync<JsonElement>($"/api/patients/{IdOf(patient)}/links", Ct);

        var link = links.EnumerateArray().Single();
        link.GetProperty("status").GetString().ShouldBe("active");
        link.GetProperty("requester").GetProperty("displayName").GetString().ShouldBe("Maria Dela Cruz");
        link.GetProperty("reviewedBy").GetString().ShouldNotBeNullOrEmpty();
        link.GetProperty("canViewRecords").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Acting_on_a_stale_version_is_rejected()
    {
        var frontDesk = await FrontDeskAsync();
        var patient = await RegisterPatientAsync(frontDesk);
        var (portal, _) = await PortalUserAsync();
        var request = await RequestAsync(portal);
        var queued = await StaffViewAsync(frontDesk, IdOf(request));
        var version = queued.GetProperty("version").GetUInt32();
        (await VerifyAsync(frontDesk, IdOf(request), patient)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await frontDesk.PutAsJsonAsync($"{Links}/{IdOf(request)}/permissions", new { canBook = false, canViewRecords = false, version }, Ct);

        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
    }
}

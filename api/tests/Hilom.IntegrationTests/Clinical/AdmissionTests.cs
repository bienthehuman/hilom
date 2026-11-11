using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Hilom.IntegrationTests.Workforce;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Clinical;

[Collection(ApiTestGroup.Name)]
public sealed class AdmissionTests(HilomApiFactory factory)
{
    private const string Url = "/api/clinical/admissions";
    private const string Encounters = "/api/clinical/encounters";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    /// <summary>A physician, a patient and a ward with two beds.</summary>
    private async Task<(BookingFixture Fixture, string PhysicianId, string PatientId, string WardId, string[] BedIds)> ReadyAsync()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var (ward, beds) = await WardWithBedsAsync(fixture.Staff.Admin, "1", "2");
        return (fixture, physician, patient, ward, beds);
    }

    private static async Task<(string WardId, string[] BedIds)> WardWithBedsAsync(HttpClient admin, params string[] labels)
    {
        var ward = await admin.PostAsJsonAsync(
            "/api/facility/wards",
            new { code = StaffFixture.Marker(), name = "Medical ward", wardType = "general" },
            Ct);
        ward.StatusCode.ShouldBe(HttpStatusCode.Created);
        var wardId = IdOf(await ward.ReadJsonAsync());
        var beds = new List<string>();
        foreach (var label in labels)
        {
            var bed = await admin.PostAsJsonAsync($"/api/facility/wards/{wardId}/beds", new { label = $"Bed {label}" }, Ct);
            bed.StatusCode.ShouldBe(HttpStatusCode.Created);
            beds.Add(IdOf(await bed.ReadJsonAsync()));
        }

        return (wardId, [.. beds]);
    }

    private static Task<HttpResponseMessage> AdmitAsync(
        HttpClient client,
        string patientId,
        string physicianId,
        string bedId,
        string? parentEncounterId = null,
        string admittingDiagnosis = "Community-acquired pneumonia",
        string reason = "Needs IV antibiotics and oxygen") =>
        client.PostAsJsonAsync(
            Url,
            new { patientId, parentEncounterId, attendingPhysicianId = physicianId, bedId, admittingDiagnosis, reason },
            Ct);

    private static async Task<string> AdmittedAsync(HttpClient client, string patientId, string physicianId, string bedId, string? parentEncounterId = null)
    {
        var response = await AdmitAsync(client, patientId, physicianId, bedId, parentEncounterId);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.ReadJsonAsync()).GetProperty("encounterId").GetString()!;
    }

    private static async Task<string[]> FreeBedsAsync(HttpClient client, string wardId)
    {
        var wards = await client.GetFromJsonAsync<JsonElement>($"{Url}/free-beds?wardId={wardId}", Ct);
        return [.. wards.EnumerateArray().SelectMany(w => w.GetProperty("beds").EnumerateArray()).Select(IdOf)];
    }

    private async Task<long> CountAsync(string sql, string patientId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("patient", Guid.Parse(patientId));
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_front_desk_admits_a_patient_directly_into_a_free_bed()
    {
        var (fixture, physician, patient, ward, beds) = await ReadyAsync();
        (await FreeBedsAsync(fixture.FrontDesk, ward)).ShouldBe(beds);

        var encounterId = await AdmittedAsync(fixture.FrontDesk, patient, physician, beds[0]);

        (await FreeBedsAsync(fixture.FrontDesk, ward)).ShouldBe([beds[1]]);
        var encounter = await fixture.Staff.Admin.GetFromJsonAsync<JsonElement>($"{Encounters}/{encounterId}", Ct);
        encounter.GetProperty("type").GetString().ShouldBe("inpatient");
        encounter.GetProperty("status").GetString().ShouldBe("open");
        encounter.GetProperty("chiefComplaint").GetString().ShouldBe("Needs IV antibiotics and oxygen");
        encounter.GetProperty("attendingPhysician").GetProperty("id").GetString().ShouldBe(physician);
        encounter.GetProperty("parentEncounterId").ValueKind.ShouldBe(JsonValueKind.Null);
        var admission = encounter.GetProperty("admission");
        admission.GetProperty("admittingDiagnosis").GetString().ShouldBe("Community-acquired pneumonia");
        admission.GetProperty("ward").GetProperty("id").GetString().ShouldBe(ward);
        admission.GetProperty("bed").GetProperty("label").GetString().ShouldBe("Bed 1");
        admission.GetProperty("dischargedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient)
            && (e.Event == AuditEvents.Clinical.EncounterOpened.Key || e.Event == AuditEvents.Clinical.PatientAdmitted.Key)));
        audit.Count.ShouldBe(2);
        audit.Select(e => e.CorrelationId).Distinct().Count().ShouldBe(1);
        var admitted = audit.Single(e => e.Event == AuditEvents.Clinical.PatientAdmitted.Key);
        admitted.Severity.ShouldBe(AuditSeverity.Notice);
        admitted.Details!.ShouldContain("Bed 1");

        // The diagnosis and reason are clinical text: never in the audit log.
        var all = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient)));
        all.ShouldAllBe(e => (e.Details == null || !e.Details.Contains("pneumonia")) && (e.Changes == null || !e.Changes.Contains("pneumonia")));
    }

    [Fact]
    public async Task The_physician_admits_from_a_clinic_visit_and_the_two_are_linked()
    {
        var (fixture, physician, patient, appointment) = await EncounterFixture.DueNowAsync(factory);
        var visitId = await EncounterFixture.CheckedInAsync(fixture, appointment);
        var (_, beds) = await WardWithBedsAsync(fixture.Staff.Admin, "1");
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        (await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{visitId}", Ct)).GetProperty("canAdmit").GetBoolean().ShouldBeTrue();

        var stayId = await AdmittedAsync(doctor, patient, physician, beds[0], visitId);

        var stay = await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{stayId}", Ct);
        stay.GetProperty("parentEncounterId").GetString().ShouldBe(visitId);
        stay.GetProperty("canAdmit").GetBoolean().ShouldBeFalse();
        var visit = await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{visitId}", Ct);
        visit.GetProperty("admittedAsEncounterId").GetString().ShouldBe(stayId);
        visit.GetProperty("canAdmit").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_encounter_list_can_be_narrowed_to_stays_or_visits_and_rejects_an_unknown_type()
    {
        var (fixture, physician, patient, appointment) = await EncounterFixture.DueNowAsync(factory);
        var visitId = await EncounterFixture.CheckedInAsync(fixture, appointment);
        var (_, beds) = await WardWithBedsAsync(fixture.Staff.Admin, "1");
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var stayId = await AdmittedAsync(doctor, patient, physician, beds[0], visitId);

        var stays = await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}?status=open&type=inpatient", Ct);
        stays.GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([stayId]);

        var visits = await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}?type=outpatient", Ct);
        visits.GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([visitId]);

        (await doctor.GetAsync($"{Encounters}?type=nonsense", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_patient_is_admitted_only_once_at_a_time_and_the_database_enforces_it()
    {
        var (fixture, physician, patient, _, beds) = await ReadyAsync();
        var encounterId = await AdmittedAsync(fixture.FrontDesk, patient, physician, beds[0]);

        var again = await AdmitAsync(fixture.FrontDesk, patient, physician, beds[1]);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("patient_already_admitted");

        // Below the API: a second open admission for the patient is rejected by the exclusion constraint.
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            with stay as (
              insert into clinical.encounters (id, patient_id, encounter_type, attending_physician_id, chief_complaint, status, started_at, created_at, updated_at)
              select @id, patient_id, 'inpatient', attending_physician_id, 'x', 'open', now(), now(), now()
              from clinical.encounters where id = @existing
              returning id, patient_id
            )
            insert into clinical.admissions (encounter_id, patient_id, admitted_at, admitting_diagnosis, reason, created_at, updated_at)
            select id, patient_id, now(), 'x', 'x', now(), now() from stay
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("existing", Guid.Parse(encounterId));
        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        error.ConstraintName.ShouldBe("ex_admissions_one_per_patient");
    }

    [Fact]
    public async Task Two_admissions_racing_for_one_bed_leave_one_patient_in_it_and_nothing_of_the_other()
    {
        var (fixture, physician, first, _, beds) = await ReadyAsync();
        var second = await fixture.PatientAsync();

        var responses = await Task.WhenAll(
            AdmitAsync(fixture.FrontDesk, first, physician, beds[0]),
            AdmitAsync(fixture.Staff.Admin, second, physician, beds[0]));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await loser.ProblemCodeAsync()).ShouldBe("bed_taken");

        // One transaction: the patient who lost the bed has no encounter or admission left behind.
        var loserId = responses[0] == loser ? first : second;
        (await CountAsync("select count(*) from clinical.encounters where patient_id = @patient", loserId)).ShouldBe(0);
        (await CountAsync("select count(*) from clinical.admissions where patient_id = @patient", loserId)).ShouldBe(0);
    }

    [Fact]
    public async Task One_patient_admitted_twice_at_once_gets_one_stay()
    {
        var (fixture, physician, patient, _, beds) = await ReadyAsync();

        var responses = await Task.WhenAll(
            AdmitAsync(fixture.FrontDesk, patient, physician, beds[0]),
            AdmitAsync(fixture.Staff.Admin, patient, physician, beds[1]));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await loser.ProblemCodeAsync()).ShouldBe("patient_already_admitted");
        (await CountAsync("select count(*) from clinical.encounters where patient_id = @patient", patient)).ShouldBe(1);
        (await CountAsync("select count(*) from clinical.bed_assignments b join clinical.admissions a on a.encounter_id = b.admission_id where a.patient_id = @patient", patient))
            .ShouldBe(1);
    }

    [Fact]
    public async Task The_bed_picker_offers_only_free_active_beds_and_admitting_checks_it()
    {
        var (fixture, physician, patient, ward, beds) = await ReadyAsync();
        var deactivated = await fixture.Staff.Admin.PostAsync($"/api/facility/beds/{beds[1]}/deactivate", null, Ct);
        deactivated.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deactivated.Content.ReadAsStringAsync(Ct));

        (await FreeBedsAsync(fixture.FrontDesk, ward)).ShouldBe([beds[0]]);
        var outOfService = await AdmitAsync(fixture.FrontDesk, patient, physician, beds[1]);

        outOfService.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await outOfService.ProblemCodeAsync()).ShouldBe("bed_not_available");
    }

    [Fact]
    public async Task Admitting_rejects_bad_input_with_problem_details()
    {
        var (fixture, physician, patient, _, beds) = await ReadyAsync();
        var (_, _, otherPatient, appointment) = await EncounterFixture.DueNowAsync(factory);
        var otherVisit = await EncounterFixture.CheckedInAsync(fixture, appointment);
        var nurse = await fixture.Staff.NurseAsync();

        var blank = await AdmitAsync(fixture.FrontDesk, patient, physician, beds[0], admittingDiagnosis: " ");
        var unknownPatient = await AdmitAsync(fixture.FrontDesk, Guid.CreateVersion7().ToString(), physician, beds[0]);
        var notAPhysician = await AdmitAsync(fixture.FrontDesk, patient, nurse, beds[0]);
        var someoneElsesVisit = await AdmitAsync(fixture.FrontDesk, patient, physician, beds[0], otherVisit);

        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await blank.ReadJsonAsync()).GetProperty("errors").TryGetProperty("admittingDiagnosis", out _).ShouldBeTrue();
        unknownPatient.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknownPatient.ProblemCodeAsync()).ShouldBe("patient_not_found");
        notAPhysician.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await notAPhysician.ProblemCodeAsync()).ShouldBe("physician_not_available");
        someoneElsesVisit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await someoneElsesVisit.ProblemCodeAsync()).ShouldBe("invalid_parent_encounter");
        otherPatient.ShouldNotBe(patient);
        (await CountAsync("select count(*) from clinical.encounters where patient_id = @patient", patient)).ShouldBe(0);
    }

    [Fact]
    public async Task Admitting_needs_the_admit_permission()
    {
        var (fixture, physician, patient, _, beds) = await ReadyAsync();
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        var admit = await AdmitAsync(nurse, patient, physician, beds[0]);
        var freeBeds = await nurse.GetAsync($"{Url}/free-beds", Ct);
        var physicians = await nurse.GetAsync($"{Url}/physicians", Ct);

        admit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        freeBeds.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        physicians.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nurses_reach_the_stays_and_patients_in_beds_on_their_ward_today_and_no_others()
    {
        var (fixture, physician, patient, ward, beds) = await ReadyAsync();
        var encounterId = await AdmittedAsync(fixture.FrontDesk, patient, physician, beds[0]);
        var (otherWard, _) = await WardWithBedsAsync(fixture.Staff.Admin, "1");
        var onWard = await fixture.Staff.NurseAsync("Santos");
        var elsewhere = await fixture.Staff.NurseAsync("Garcia");
        await fixture.Staff.AssignAsync(onWard, ward, StaffFixture.Today);
        await fixture.Staff.AssignAsync(elsewhere, otherWard, StaffFixture.Today);
        var nurse = await fixture.Staff.SignInAsEmployeeAsync(onWard, "Staff Nurse");
        var outsider = await fixture.Staff.SignInAsEmployeeAsync(elsewhere, "Staff Nurse");

        var list = await nurse.GetFromJsonAsync<JsonElement>($"{Encounters}?patientId={patient}", Ct);
        var opened = await nurse.GetAsync($"{Encounters}/{encounterId}", Ct);
        var chart = await nurse.GetAsync($"/api/patients/{patient}", Ct);
        var outsiderList = await outsider.GetFromJsonAsync<JsonElement>($"{Encounters}?patientId={patient}", Ct);
        var outsiderOpened = await outsider.GetAsync($"{Encounters}/{encounterId}", Ct);
        var outsiderChart = await outsider.GetAsync($"/api/patients/{patient}", Ct);

        list.GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([encounterId]);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await opened.ReadJsonAsync()).GetProperty("canRecordVitals").GetBoolean().ShouldBeTrue();
        chart.StatusCode.ShouldBe(HttpStatusCode.OK);
        outsiderList.GetProperty("total").GetInt32().ShouldBe(0);
        outsiderOpened.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        outsiderChart.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(encounterId) && e.Event == AuditEvents.Access.Denied.Key)))
            .Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_database_keeps_a_bed_assignments_ward_the_beds_ward()
    {
        var (fixture, physician, patient, _, beds) = await ReadyAsync();
        await AdmittedAsync(fixture.FrontDesk, patient, physician, beds[0]);
        var (otherWard, _) = await WardWithBedsAsync(fixture.Staff.Admin, "1");

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "update clinical.bed_assignments set ward_id = @ward where bed_id = @bed",
            connection);
        command.Parameters.AddWithValue("ward", Guid.Parse(otherWard));
        command.Parameters.AddWithValue("bed", Guid.Parse(beds[0]));
        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        error.ConstraintName.ShouldBe("fk_bed_assignments_beds_bed_id_ward_id");
    }
}

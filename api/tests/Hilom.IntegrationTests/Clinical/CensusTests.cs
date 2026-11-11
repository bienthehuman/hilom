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
public sealed class CensusTests(HilomApiFactory factory)
{
    private const string Url = "/api/clinical/census";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    private sealed record Ward3(
        BookingFixture Fixture,
        string PhysicianId,
        string WardId,
        string[] BedIds,
        string PatientId,
        string EncounterId,
        HttpClient Nurse);

    /// <summary>A ward with three beds, a patient admitted to the second, and a nurse assigned to the ward today.</summary>
    private async Task<Ward3> AdmittedAsync()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var (ward, beds) = await WardWithBedsAsync(fixture.Staff.Admin, "1", "2", "10");
        var encounterId = await AdmitAsync(fixture.FrontDesk, patient, physician, beds[1]);
        var nurseId = await fixture.Staff.NurseAsync();
        await fixture.Staff.AssignAsync(nurseId, ward, StaffFixture.Today);
        var nurse = await fixture.Staff.SignInAsEmployeeAsync(nurseId, "Staff Nurse");
        return new Ward3(fixture, physician, ward, beds, patient, encounterId, nurse);
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

    private static async Task<string> AdmitAsync(HttpClient client, string patientId, string physicianId, string bedId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/clinical/admissions",
            new { patientId, attendingPhysicianId = physicianId, bedId, admittingDiagnosis = "Pneumonia", reason = "IV antibiotics" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.ReadJsonAsync()).GetProperty("encounterId").GetString()!;
    }

    private static async Task AllergyAsync(HttpClient client, string patientId, string allergen, string severity)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/allergies",
            new { allergen, category = "drug", severity, reaction = "Hives" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string> VitalsAsync(HttpClient client, string encounterId, object values)
    {
        var response = await client.PostAsJsonAsync($"/api/clinical/encounters/{encounterId}/vital-signs", values, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return IdOf(await response.ReadJsonAsync());
    }

    private static async Task<JsonElement> BoardAsync(HttpClient client, string? wardId = null)
    {
        var response = await client.GetAsync(wardId is null ? Url : $"{Url}?wardId={wardId}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static JsonElement WardOf(JsonElement board, string wardId) =>
        board.EnumerateArray().Single(w => IdOf(w) == wardId);

    private static JsonElement BedOf(JsonElement ward, string bedId) =>
        ward.GetProperty("beds").EnumerateArray().Single(b => IdOf(b) == bedId);

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public async Task A_nurse_sees_every_bed_of_their_ward_with_who_is_in_it()
    {
        var w = await AdmittedAsync();

        var board = await BoardAsync(w.Nurse);

        board.GetArrayLength().ShouldBe(1);
        var ward = WardOf(board, w.WardId);
        ward.GetProperty("occupied").GetInt32().ShouldBe(1);
        // Natural order: Bed 2 before Bed 10.
        ward.GetProperty("beds").EnumerateArray().Select(b => b.GetProperty("label").GetString()).ShouldBe(["Bed 1", "Bed 2", "Bed 10"]);
        BedOf(ward, w.BedIds[0]).GetProperty("patient").ValueKind.ShouldBe(JsonValueKind.Null);
        var patient = BedOf(ward, w.BedIds[1]).GetProperty("patient");
        IdOf(patient).ShouldBe(w.PatientId);
        patient.GetProperty("encounterId").GetString().ShouldBe(w.EncounterId);
        patient.GetProperty("canOpen").GetBoolean().ShouldBeTrue();
        patient.GetProperty("attendingPhysician").GetProperty("id").GetString().ShouldBe(w.PhysicianId);
        patient.GetProperty("admittedHours").GetInt32().ShouldBe(0);
        patient.GetProperty("severeAllergies").GetArrayLength().ShouldBe(0);
        patient.GetProperty("criticalVitals").GetArrayLength().ShouldBe(0);
        patient.GetProperty("lastVitalsAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Tiles_flag_severe_allergies_only()
    {
        var w = await AdmittedAsync();
        await AllergyAsync(w.Fixture.Staff.Admin, w.PatientId, "Penicillin", "severe");
        await AllergyAsync(w.Fixture.Staff.Admin, w.PatientId, "Aspirin", "severe");
        await AllergyAsync(w.Fixture.Staff.Admin, w.PatientId, "Shellfish", "moderate");

        var patient = BedOf(WardOf(await BoardAsync(w.Nurse), w.WardId), w.BedIds[1]).GetProperty("patient");

        Strings(patient.GetProperty("severeAllergies")).ShouldBe(["Aspirin", "Penicillin"]);
    }

    [Fact]
    public async Task Tiles_flag_critical_values_in_the_latest_vital_signs_and_skip_voided_ones()
    {
        var w = await AdmittedAsync();
        async Task<JsonElement> Patient() =>
            BedOf(WardOf(await BoardAsync(w.Nurse), w.WardId), w.BedIds[1]).GetProperty("patient");

        await VitalsAsync(w.Nurse, w.EncounterId, new { heartRate = 140, oxygenSaturation = 85, temperatureC = 38.5m });
        Strings((await Patient()).GetProperty("criticalVitals")).ShouldBe(["heartRate", "oxygenSaturation"]);

        // A later normal set clears the flag: the board shows how the patient is now.
        var later = DateTimeOffset.UtcNow.AddMinutes(1);
        var normal = await VitalsAsync(w.Nurse, w.EncounterId, new { recordedAt = later, heartRate = 80, oxygenSaturation = 98 });
        var cleared = await Patient();
        cleared.GetProperty("criticalVitals").GetArrayLength().ShouldBe(0);
        cleared.GetProperty("lastVitalsAt").GetDateTimeOffset().ShouldBe(later, TimeSpan.FromSeconds(1));

        // Voiding that set (entered in error) brings back the one before it.
        var voided = await w.Nurse.PostAsJsonAsync(
            $"/api/clinical/encounters/{w.EncounterId}/vital-signs/{normal}/void",
            new { reason = "Wrong patient" },
            Ct);
        voided.StatusCode.ShouldBe(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync(Ct));
        Strings((await Patient()).GetProperty("criticalVitals")).ShouldBe(["heartRate", "oxygenSaturation"]);
    }

    [Fact]
    public async Task The_board_follows_the_wards_a_nurse_is_on_today()
    {
        var w = await AdmittedAsync();
        var (otherWard, _) = await WardWithBedsAsync(w.Fixture.Staff.Admin, "1");
        var unassigned = await w.Fixture.Staff.SignInAsEmployeeAsync(await w.Fixture.Staff.NurseAsync("Garcia"), "Staff Nurse");
        var elsewhereId = await w.Fixture.Staff.NurseAsync("Cruz");
        await w.Fixture.Staff.AssignAsync(elsewhereId, otherWard, StaffFixture.Today);
        var elsewhere = await w.Fixture.Staff.SignInAsEmployeeAsync(elsewhereId, "Staff Nurse");

        (await BoardAsync(unassigned)).GetArrayLength().ShouldBe(0);
        var theirs = await BoardAsync(elsewhere);
        theirs.EnumerateArray().Select(IdOf).ShouldBe([otherWard]);
    }

    [Fact]
    public async Task Asking_for_a_ward_outside_the_callers_reach_is_not_found_and_logged()
    {
        var w = await AdmittedAsync();
        var (otherWard, _) = await WardWithBedsAsync(w.Fixture.Staff.Admin, "1");

        var mine = await BoardAsync(w.Nurse, w.WardId);
        var outside = await w.Nurse.GetAsync($"{Url}?wardId={otherWard}", Ct);
        var unknown = await w.Nurse.GetAsync($"{Url}?wardId={Guid.CreateVersion7()}", Ct);

        mine.EnumerateArray().Select(IdOf).ShouldBe([w.WardId]);
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outside.ProblemCodeAsync()).ShouldBe("ward_not_found");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(otherWard) && e.Event == AuditEvents.Access.Denied.Key)))
            .Count.ShouldBe(1);
    }

    [Fact]
    public async Task Roles_with_all_see_every_ward_but_open_only_the_stays_they_reach()
    {
        var w = await AdmittedAsync();
        var (otherWard, _) = await WardWithBedsAsync(w.Fixture.Staff.Admin, "1");

        var desk = await BoardAsync(w.Fixture.FrontDesk);

        desk.EnumerateArray().Select(IdOf).ShouldContain(w.WardId);
        desk.EnumerateArray().Select(IdOf).ShouldContain(otherWard);
        // The front desk sees the board but can't open clinical encounters.
        BedOf(WardOf(desk, w.WardId), w.BedIds[1]).GetProperty("patient").GetProperty("canOpen").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_board_needs_the_census_permission()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");

        var response = await hr.GetAsync(Url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_closed_bed_assignment_frees_the_bed_on_the_board()
    {
        var w = await AdmittedAsync();

        // Transfers and discharges (3.6) close the bed assignment; the view only shows open ones.
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                "update clinical.bed_assignments set during = tstzrange(lower(during), now()) where bed_id = @bed",
                connection);
            command.Parameters.AddWithValue("bed", Guid.Parse(w.BedIds[1]));
            (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }

        var ward = WardOf(await BoardAsync(w.Fixture.Staff.Admin, w.WardId), w.WardId);

        ward.GetProperty("occupied").GetInt32().ShouldBe(0);
        BedOf(ward, w.BedIds[1]).GetProperty("patient").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}

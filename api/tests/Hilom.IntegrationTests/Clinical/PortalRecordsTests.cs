using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Hilom.IntegrationTests.Workforce;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Clinical;

/// <summary>
/// Slice 3.9: a linked portal user with <c>can_view_records</c> reads visit and discharge summaries, vitals and
/// appointments, and nothing else. A link without it sees nothing.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PortalRecordsTests(HilomApiFactory factory)
{
    private const string Encounters = "/api/clinical/encounters";
    private const string NursingSecret = "NURSING-NOTE-SECRET-TEXT";
    private const string VitalsNoteSecret = "VITALS-STAFF-NOTE-SECRET";
    private const string OrderSecret = "ORDER-DETAILS-SECRET";
    private const string Summary = "Pneumonia resolved on IV antibiotics. Home on oral amoxicillin for 5 days.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    private static string RecordsUrl(string patientId) => $"/api/clinical/portal-records/{patientId}";

    /// <summary>
    /// A discharged ward stay with everything on it: two vitals sets (one voided), a nursing note, a medication
    /// order with a dose, and a discharge summary. Plus one booked appointment.
    /// </summary>
    private sealed record Chart(BookingFixture Fixture, string PatientId, string EncounterId, HttpClient Nurse, HttpClient Doctor);

    private async Task<Chart> ChartAsync()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var ward = await fixture.Staff.WardAsync();
        var bed = await fixture.Staff.Admin.PostAsJsonAsync($"/api/facility/wards/{ward}/beds", new { label = "Bed 1" }, Ct);
        var admitted = await fixture.FrontDesk.PostAsJsonAsync(
            "/api/clinical/admissions",
            new { patientId = patient, attendingPhysicianId = physician, bedId = IdOf(await bed.ReadJsonAsync()), admittingDiagnosis = "Pneumonia", reason = "IV antibiotics" },
            Ct);
        admitted.StatusCode.ShouldBe(HttpStatusCode.Created, await admitted.Content.ReadAsStringAsync(Ct));
        var encounterId = (await admitted.ReadJsonAsync()).GetProperty("encounterId").GetString()!;

        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var nurseId = await fixture.Staff.NurseAsync();
        await fixture.Staff.AssignAsync(nurseId, ward, StaffFixture.Today);
        var nurse = await fixture.Staff.SignInAsEmployeeAsync(nurseId, "Staff Nurse");

        (await nurse.PostAsJsonAsync($"{Encounters}/{encounterId}/vital-signs", new { temperatureC = 38.4, heartRate = 150, notes = VitalsNoteSecret }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var wrong = await nurse.PostAsJsonAsync($"{Encounters}/{encounterId}/vital-signs", new { temperatureC = 31.0 }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await nurse.PostAsJsonAsync($"{Encounters}/{encounterId}/vital-signs/{IdOf(await wrong.ReadJsonAsync())}/void", new { reason = "Wrong patient" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await nurse.PostAsJsonAsync($"{Encounters}/{encounterId}/notes", new { noteType = "nursing", body = NursingSecret }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var order = await doctor.PostAsJsonAsync(
            $"{Encounters}/{encounterId}/orders",
            new { orderType = "medication", details = OrderSecret, medicationName = "Ceftriaxone", dose = "500 mg", route = "oral", frequency = "every 8 hours" },
            Ct);
        order.StatusCode.ShouldBe(HttpStatusCode.Created, await order.Content.ReadAsStringAsync(Ct));
        (await nurse.PostAsJsonAsync(
            $"{Encounters}/{encounterId}/medication-administrations",
            new { orderId = IdOf(await order.ReadJsonAsync()), outcome = "given", doseGiven = "500 mg" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var stay = await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{encounterId}", Ct);
        var discharged = await doctor.PostAsJsonAsync(
            $"/api/clinical/admissions/{encounterId}/discharge",
            new { disposition = "home", summary = Summary, version = stay.GetProperty("admission").GetProperty("version").GetUInt32() },
            Ct);
        discharged.StatusCode.ShouldBe(HttpStatusCode.OK, await discharged.Content.ReadAsStringAsync(Ct));

        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        (await BookingFixture.BookAsync(fixture.FrontDesk, slot, patient, "Follow-up after pneumonia")).StatusCode.ShouldBe(HttpStatusCode.Created);
        return new Chart(fixture, patient, encounterId, nurse, doctor);
    }

    [Fact]
    public async Task A_link_with_can_view_records_reads_summaries_vitals_and_appointments_and_nothing_else()
    {
        var chart = await ChartAsync();
        var (portal, userId, linkId) = await chart.Fixture.LinkedPortalUserAsync(chart.PatientId, canBook: false, canViewRecords: true);

        var response = await portal.GetAsync(RecordsUrl(chart.PatientId), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var records = JsonDocument.Parse(body).RootElement;
        records.EnumerateObject().Select(p => p.Name).ShouldBe(["patientId", "visits", "vitals", "appointments"]);

        var visit = records.GetProperty("visits").EnumerateArray().Single();
        visit.GetProperty("id").GetString().ShouldBe(chart.EncounterId);
        visit.GetProperty("dischargeSummary").GetString().ShouldBe(Summary);
        visit.GetProperty("dischargeDisposition").GetString().ShouldBe("home");
        visit.GetProperty("status").GetString().ShouldBe("closed");

        var vitals = records.GetProperty("vitals").EnumerateArray().Single();
        vitals.GetProperty("temperatureC").GetDecimal().ShouldBe(38.4m);
        vitals.GetProperty("flags").GetProperty("heartRate").GetString().ShouldBe("critical_high");

        var appointment = records.GetProperty("appointments").EnumerateArray().Single();
        appointment.GetProperty("reason").GetString().ShouldBe("Follow-up after pneumonia");

        // Nursing notes, staff comments on vitals, orders and the medication record never leave.
        foreach (var secret in new[] { NursingSecret, VitalsNoteSecret, OrderSecret, "Ceftriaxone", "doseGiven" })
        {
            body.ShouldNotContain(secret);
        }

        // The view is logged as a record view, naming the link it went through.
        var views = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == userId && e.Event == AuditEvents.Clinical.EncounterViewed.Key));
        views.Single().ActingForPatientLinkId.ShouldBe(Guid.Parse(linkId));
        views.Single().PatientId.ShouldBe(Guid.Parse(chart.PatientId));
    }

    [Fact]
    public async Task A_link_without_can_view_records_sees_nothing()
    {
        var chart = await ChartAsync();
        var (portal, userId, _) = await chart.Fixture.LinkedPortalUserAsync(chart.PatientId, canBook: true, canViewRecords: false);

        (await portal.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await portal.GetAsync($"/api/patients/{chart.PatientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await portal.GetAsync($"/api/patients/{chart.PatientId}/allergies", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertNoStaffRecordsAsync(portal, chart);

        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == userId && e.Event == AuditEvents.Access.Denied.Key));
        denied.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_link_with_can_view_records_still_cannot_reach_staff_record_endpoints()
    {
        var chart = await ChartAsync();
        var (portal, _, _) = await chart.Fixture.LinkedPortalUserAsync(chart.PatientId, canBook: true, canViewRecords: true);

        (await portal.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The portal-safe view is the only door: nursing notes, orders, the MAR and the raw encounter stay shut.
        await AssertNoStaffRecordsAsync(portal, chart);
    }

    private static async Task AssertNoStaffRecordsAsync(HttpClient portal, Chart chart)
    {
        var encounter = $"{Encounters}/{chart.EncounterId}";
        foreach (var url in new[]
        {
            encounter,
            $"{encounter}/vital-signs",
            $"{encounter}/notes",
            $"{encounter}/orders",
            $"{encounter}/medication-administrations",
        })
        {
            (await portal.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, url);
        }

        var list = await portal.GetFromJsonAsync<JsonElement>($"{Encounters}?patientId={chart.PatientId}", Ct);
        list.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Another_patients_records_stay_closed_even_with_a_records_link_to_someone_else()
    {
        var chart = await ChartAsync();
        var (portal, _, _) = await chart.Fixture.LinkedPortalUserAsync(await chart.Fixture.PatientAsync(), canBook: true, canViewRecords: true);

        (await portal.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoking_the_link_closes_the_records_on_the_next_request()
    {
        var chart = await ChartAsync();
        var (portal, _, linkId) = await chart.Fixture.LinkedPortalUserAsync(chart.PatientId, canBook: true, canViewRecords: true);
        (await portal.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // This patient's own links, so the shared database's other active links can't push it off a page.
        var active = (await chart.Fixture.FrontDesk.GetFromJsonAsync<JsonElement>($"/api/patients/{chart.PatientId}/links", Ct))
            .EnumerateArray().Single(l => IdOf(l) == linkId);
        (await chart.Fixture.FrontDesk.PostAsJsonAsync(
            $"/api/patients/links/{linkId}/revoke",
            new { reason = "Patient asked to remove access", version = active.GetProperty("version").GetUInt32() },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await portal.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_use_the_encounter_endpoints_not_the_portal_view()
    {
        var chart = await ChartAsync();

        // A ward nurse holds the permission, but not at linked scope: no link, so nothing here.
        (await chart.Nurse.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await chart.Doctor.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The front desk has no encounter permission at all.
        (await chart.Fixture.FrontDesk.GetAsync(RecordsUrl(chart.PatientId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

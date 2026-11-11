using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Clinical;

[Collection(ApiTestGroup.Name)]
public sealed class EncounterTests(HilomApiFactory factory)
{
    private const string Url = "/api/clinical/encounters";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    private static uint VersionOf(JsonElement element) => element.GetProperty("version").GetUInt32();

    private Task<(BookingFixture Fixture, string PhysicianId, string PatientId, JsonElement Appointment)> ConfirmedAsync() =>
        EncounterFixture.ConfirmedAsync(factory);

    private Task<(BookingFixture Fixture, string PhysicianId, string PatientId, JsonElement Appointment)> DueNowAsync() =>
        EncounterFixture.DueNowAsync(factory);

    private static Task<HttpResponseMessage> CheckInAsync(HttpClient client, JsonElement appointment) =>
        EncounterFixture.CheckInAsync(client, appointment);

    private static Task<string> CheckedInAsync(BookingFixture fixture, JsonElement appointment) =>
        EncounterFixture.CheckedInAsync(fixture, appointment);

    [Fact]
    public async Task Checking_in_opens_an_outpatient_encounter_with_the_appointments_physician()
    {
        var (fixture, physician, patient, appointment) = await DueNowAsync();

        var encounterId = await CheckedInAsync(fixture, appointment);

        var day = (await fixture.FrontDesk.GetFromJsonAsync<JsonElement>($"/api/scheduling/appointments?physicianId={physician}", Ct))
            .EnumerateArray().Single(a => IdOf(a) == IdOf(appointment));
        day.GetProperty("status").GetString().ShouldBe("checked_in");
        day.GetProperty("encounterId").GetString().ShouldBe(encounterId);
        day.GetProperty("canCancel").GetBoolean().ShouldBeFalse();

        var admin = fixture.Staff.Admin;
        var encounter = await admin.GetFromJsonAsync<JsonElement>($"{Url}/{encounterId}", Ct);
        encounter.GetProperty("type").GetString().ShouldBe("outpatient");
        encounter.GetProperty("status").GetString().ShouldBe("open");
        encounter.GetProperty("chiefComplaint").GetString().ShouldBe("Chest pain when climbing stairs");
        encounter.GetProperty("attendingPhysician").GetProperty("id").GetString().ShouldBe(physician);
        encounter.GetProperty("patient").GetProperty("id").GetString().ShouldBe(patient);
        encounter.GetProperty("appointmentId").GetString().ShouldBe(IdOf(appointment));
        encounter.GetProperty("openedBy").GetString().ShouldBe("Test User");

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient)
            && (e.Event == AuditEvents.Scheduling.AppointmentCheckedIn.Key || e.Event == AuditEvents.Clinical.EncounterOpened.Key)));
        audit.Count.ShouldBe(2);
        audit.Select(e => e.CorrelationId).Distinct().Count().ShouldBe(1);
        audit.Single(e => e.Event == AuditEvents.Clinical.EncounterOpened.Key).Severity.ShouldBe(AuditSeverity.Notice);
    }

    [Fact]
    public async Task Only_confirmed_appointments_are_checked_in_on_their_day_and_only_once()
    {
        var (fixture, _, _, nextWeek) = await ConfirmedAsync();
        var (_, _, _, dueNow) = await DueNowAsync();

        var early = await CheckInAsync(fixture.FrontDesk, nextWeek);
        var first = await CheckInAsync(fixture.FrontDesk, dueNow);
        var again = await CheckInAsync(fixture.FrontDesk, dueNow);

        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.ProblemCodeAsync()).ShouldBe("appointment_not_today");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("appointment_cannot_check_in");
    }

    [Fact]
    public async Task A_requested_appointment_must_be_confirmed_before_check_in()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        var requested = await (await BookingFixture.BookAsync(fixture.FrontDesk, slot, patient)).ReadJsonAsync();

        var response = await CheckInAsync(fixture.FrontDesk, requested);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("appointment_cannot_check_in");
    }

    [Fact]
    public async Task Checking_in_needs_the_check_in_permission()
    {
        var (fixture, physician, _, appointment) = await DueNowAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");

        var response = await CheckInAsync(doctor, appointment);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_attending_physician_sees_their_encounters_and_a_colleague_does_not()
    {
        var (fixture, physician, patient, appointment) = await DueNowAsync();
        var encounterId = await CheckedInAsync(fixture, appointment);
        var (colleagueId, _) = await fixture.PhysicianWithSlotsAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var colleague = await fixture.Staff.SignInAsEmployeeAsync(colleagueId, "Physician");

        var mine = await doctor.GetFromJsonAsync<JsonElement>($"{Url}?status=open", Ct);
        var theirs = await colleague.GetFromJsonAsync<JsonElement>(Url, Ct);
        var opened = await doctor.GetAsync($"{Url}/{encounterId}", Ct);
        var openedAgain = await doctor.GetAsync($"{Url}/{encounterId}", Ct);
        var byColleague = await colleague.GetAsync($"{Url}/{encounterId}", Ct);

        mine.GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([encounterId]);
        theirs.GetProperty("total").GetInt32().ShouldBe(0);
        opened.StatusCode.ShouldBe(HttpStatusCode.OK);
        openedAgain.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await opened.ReadJsonAsync()).GetProperty("canManage").GetBoolean().ShouldBeTrue();
        byColleague.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await byColleague.ProblemCodeAsync()).ShouldBe("encounter_not_found");

        var views = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.EncounterViewed.Key));
        views.Count.ShouldBe(1);
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(encounterId) && e.Event == AuditEvents.Access.Denied.Key));
        denied.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_front_desk_cannot_see_encounters_and_a_staff_nurse_without_a_ward_reaches_none()
    {
        var (fixture, _, _, appointment) = await DueNowAsync();
        var encounterId = await CheckedInAsync(fixture, appointment);
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        var byFrontDesk = await fixture.FrontDesk.GetAsync(Url, Ct);
        var nurseList = await nurse.GetFromJsonAsync<JsonElement>(Url, Ct);
        var nurseOpen = await nurse.GetAsync($"{Url}/{encounterId}", Ct);

        byFrontDesk.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        nurseList.GetProperty("total").GetInt32().ShouldBe(0);
        nurseOpen.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_physician_updates_the_summary_and_closing_completes_the_appointment()
    {
        var (fixture, physician, patient, appointment) = await DueNowAsync();
        var encounterId = await CheckedInAsync(fixture, appointment);
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var encounter = await doctor.GetFromJsonAsync<JsonElement>($"{Url}/{encounterId}", Ct);

        var updated = await doctor.PutAsJsonAsync(
            $"{Url}/{encounterId}",
            new { chiefComplaint = "Chest pain on exertion, 2 weeks", diagnosisSummary = "Stable angina, for work-up", version = VersionOf(encounter) },
            Ct);
        var stale = await doctor.PutAsJsonAsync(
            $"{Url}/{encounterId}",
            new { chiefComplaint = "Something else", diagnosisSummary = (string?)null, version = VersionOf(encounter) },
            Ct);
        var blank = await doctor.PutAsJsonAsync($"{Url}/{encounterId}", new { chiefComplaint = " ", version = VersionOf(encounter) }, Ct);

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        var afterUpdate = await updated.ReadJsonAsync();
        afterUpdate.GetProperty("diagnosisSummary").GetString().ShouldBe("Stable angina, for work-up");
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var closed = await doctor.PostAsJsonAsync($"{Url}/{encounterId}/close", new { version = VersionOf(afterUpdate) }, Ct);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Ct));
        var afterClose = await closed.ReadJsonAsync();
        afterClose.GetProperty("status").GetString().ShouldBe("closed");
        afterClose.GetProperty("endedAt").ValueKind.ShouldBe(JsonValueKind.String);
        afterClose.GetProperty("canManage").GetBoolean().ShouldBeFalse();

        var day = (await fixture.FrontDesk.GetFromJsonAsync<JsonElement>($"/api/scheduling/appointments?physicianId={physician}", Ct))
            .EnumerateArray().Single(a => IdOf(a) == IdOf(appointment));
        day.GetProperty("status").GetString().ShouldBe("completed");

        var late = await doctor.PutAsJsonAsync(
            $"{Url}/{encounterId}",
            new { chiefComplaint = "Too late", version = VersionOf(afterClose) },
            Ct);
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await late.ProblemCodeAsync()).ShouldBe("encounter_not_open");

        // The audit log says which fields changed, never the clinical text.
        var changed = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.EncounterChanged.Key))).Single();
        changed.Details!.ShouldContain("diagnosisSummary");
        changed.Details!.ShouldNotContain("Stable angina");
        var dataChanges = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(encounterId) && e.Event == AuditEvents.DataChanged.Key));
        dataChanges.ShouldNotBeEmpty();
        dataChanges.ShouldAllBe(e => e.Changes == null || !e.Changes.Contains("Stable angina"));
        (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.EncounterClosed.Key))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_physicians_encounter_patients_are_their_own_patients()
    {
        var (fixture, physician, patient, appointment) = await DueNowAsync();
        await CheckedInAsync(fixture, appointment);
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");

        // Take the appointment out of the physician's reach (cancelled ones don't count), leaving only the encounter.
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                "update scheduling.appointments set status = 'cancelled', cancel_reason = 'test' where id = @id",
                connection);
            command.Parameters.AddWithValue("id", Guid.Parse(IdOf(appointment)));
            await command.ExecuteNonQueryAsync(Ct);
        }

        var response = await doctor.GetAsync($"/api/patients/{patient}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Workforce;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Scheduling;

[Collection(ApiTestGroup.Name)]
public sealed class AppointmentWorkflowTests(HilomApiFactory factory)
{
    private const string Url = "/api/scheduling/appointments";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    private static uint VersionOf(JsonElement element) => element.GetProperty("version").GetUInt32();

    /// <summary>A physician with slots, a patient booked into the first one by the front desk.</summary>
    private async Task<(BookingFixture Fixture, string PhysicianId, string PatientId, JsonElement Appointment)> BookedAsync()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        var booked = await BookingFixture.BookAsync(fixture.FrontDesk, slot, patient);
        booked.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (fixture, physician, patient, await booked.ReadJsonAsync());
    }

    private static async Task<HttpResponseMessage> ActAsync(HttpClient client, JsonElement appointment, string action, object? body = null)
    {
        var payload = body ?? new { version = VersionOf(appointment) };
        return await client.PostAsJsonAsync($"{Url}/{IdOf(appointment)}/{action}", payload, Ct);
    }

    /// <summary>Moves the appointment's slot to an hour ago, as if its time had come.</summary>
    private async Task StartedAsync(JsonElement appointment)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            update scheduling.availability_slots
            set during = tstzrange(now() - interval '1 hour', now() - interval '30 minutes')
            where id = (select slot_id from scheduling.appointments where id = @id)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.Parse(IdOf(appointment)));
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_physician_confirms_a_request_for_their_own_slot()
    {
        var (fixture, physician, patient, appointment) = await BookedAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");

        var response = await ActAsync(doctor, appointment, "confirm", new { note = "Please bring your medicines.", version = VersionOf(appointment) });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var confirmed = await response.ReadJsonAsync();
        confirmed.GetProperty("status").GetString().ShouldBe("confirmed");
        confirmed.GetProperty("decisionNote").GetString().ShouldBe("Please bring your medicines.");
        confirmed.GetProperty("decidedBy").GetString().ShouldBe("Test User");
        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Scheduling.AppointmentConfirmed.Key));
        audit.Count.ShouldBe(1);

        var again = await ActAsync(doctor, confirmed, "confirm");
        (await again.ProblemCodeAsync()).ShouldBe("appointment_not_requested");
    }

    [Fact]
    public async Task A_physician_cannot_decide_a_colleagues_request()
    {
        var (fixture, _, _, appointment) = await BookedAsync();
        var (colleague, _) = await fixture.PhysicianWithSlotsAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(colleague, "Physician");

        var response = await ActAsync(doctor, appointment, "confirm", new { version = VersionOf(appointment) });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ProblemCodeAsync()).ShouldBe("appointment_not_found");
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(IdOf(appointment)) && e.Event == AuditEvents.Access.Denied.Key));
        denied.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Declining_needs_a_reason_frees_the_slot_and_tells_the_family()
    {
        var (fixture, physician, patient, _) = await BookedAsync();
        var (portal, _, _) = await fixture.LinkedPortalUserAsync(patient, canBook: true);
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(portal, physician))[0]);
        var request = await (await BookingFixture.BookAsync(portal, slot, patient)).ReadJsonAsync();

        var withoutReason = await ActAsync(fixture.FrontDesk, request, "decline", new { reason = " ", version = VersionOf(request) });
        var declined = await ActAsync(fixture.FrontDesk, request, "decline", new { reason = "Dr. Santos is away that day. Please book another day.", version = VersionOf(request) });

        (await withoutReason.ReadJsonAsync()).GetProperty("errors").GetProperty("reason")[0].GetString()
            .ShouldBe("Say why, so the patient knows what to do next.");
        declined.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BookingFixture.OpenSlotsAsync(portal, physician)).Select(IdOf).ShouldContain(slot);
        var mine = (await portal.GetFromJsonAsync<JsonElement>($"{Url}/mine", Ct)).EnumerateArray()
            .Single(a => IdOf(a) == IdOf(request));
        mine.GetProperty("status").GetString().ShouldBe("declined");
        mine.GetProperty("decisionNote").GetString().ShouldBe("Dr. Santos is away that day. Please book another day.");
        mine.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_family_cancels_through_a_link_that_allows_booking()
    {
        var (fixture, physician, patient, appointment) = await BookedAsync();
        var (booker, bookerId, linkId) = await fixture.LinkedPortalUserAsync(patient, canBook: true);
        var (viewer, _, _) = await fixture.LinkedPortalUserAsync(patient, canBook: false, canViewRecords: true);

        var seenByViewer = (await viewer.GetFromJsonAsync<JsonElement>($"{Url}/mine", Ct)).EnumerateArray().Single();
        seenByViewer.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        var byViewer = await ActAsync(viewer, appointment, "cancel", new { reason = "x", version = VersionOf(appointment) });
        byViewer.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var seenByBooker = (await booker.GetFromJsonAsync<JsonElement>($"{Url}/mine", Ct)).EnumerateArray().Single();
        seenByBooker.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        var cancelled = await ActAsync(booker, appointment, "cancel", new { reason = "Papa is feeling better.", version = VersionOf(appointment) });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancelled.ReadJsonAsync()).GetProperty("cancelReason").GetString().ShouldBe("Papa is feeling better.");
        (await BookingFixture.OpenSlotsAsync(booker, physician)).Length.ShouldBe(2);
        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == bookerId && e.Event == AuditEvents.Scheduling.AppointmentCancelled.Key));
        audit.Single().ActingForPatientLinkId.ShouldBe(Guid.Parse(linkId));
        audit.Single().Severity.ShouldBe(AuditSeverity.Notice);
    }

    [Fact]
    public async Task A_no_show_is_marked_after_the_visit_starts_and_then_it_cannot_be_cancelled()
    {
        var (fixture, physician, _, appointment) = await BookedAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var confirmed = await (await ActAsync(fixture.FrontDesk, appointment, "confirm", new { version = VersionOf(appointment) })).ReadJsonAsync();

        var tooEarly = await ActAsync(fixture.FrontDesk, confirmed, "no-show");
        (await tooEarly.ProblemCodeAsync()).ShouldBe("appointment_not_started");

        await StartedAsync(confirmed);
        (await ActAsync(doctor, confirmed, "no-show")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var lateCancel = await ActAsync(fixture.FrontDesk, confirmed, "cancel", new { reason = "x", version = VersionOf(confirmed) });
        (await lateCancel.ProblemCodeAsync()).ShouldBe("appointment_already_started");

        var noShow = await ActAsync(fixture.FrontDesk, confirmed, "no-show");
        noShow.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await noShow.ReadJsonAsync()).GetProperty("status").GetString().ShouldBe("no_show");
    }

    [Fact]
    public async Task The_day_list_shows_every_physician_to_the_front_desk_and_only_their_own_to_a_physician()
    {
        var (fixture, physician, _, appointment) = await BookedAsync();
        var (colleague, _) = await fixture.PhysicianWithSlotsAsync();
        var other = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, colleague))[0]);
        var theirs = await (await BookingFixture.BookAsync(fixture.FrontDesk, other, await fixture.PatientAsync())).ReadJsonAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var day = StaffFixture.Day(BookingFixture.NextMonday());

        var frontDesk = await fixture.FrontDesk.GetFromJsonAsync<JsonElement>($"{Url}?from={day}", Ct);
        var mine = await doctor.GetFromJsonAsync<JsonElement>($"{Url}?from={day}", Ct);
        var inbox = await doctor.GetFromJsonAsync<JsonElement>($"{Url}?from={StaffFixture.Day(StaffFixture.Today)}&days=31&status=requested", Ct);

        frontDesk.EnumerateArray().Select(IdOf).ShouldContain(IdOf(appointment));
        frontDesk.EnumerateArray().Select(IdOf).ShouldContain(IdOf(theirs));
        mine.EnumerateArray().Select(IdOf).ShouldBe([IdOf(appointment)]);
        inbox.EnumerateArray().Select(IdOf).ShouldBe([IdOf(appointment)]);
        mine.EnumerateArray().Single().GetProperty("bookedBy").GetString().ShouldBe("Test User");
    }

    [Fact]
    public async Task Acting_on_a_stale_version_is_rejected()
    {
        var (fixture, _, _, appointment) = await BookedAsync();
        (await ActAsync(fixture.FrontDesk, appointment, "confirm", new { version = VersionOf(appointment) })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await ActAsync(fixture.FrontDesk, appointment, "cancel", new { reason = "x", version = VersionOf(appointment) });

        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Staff_without_the_permissions_get_403()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var id = Guid.CreateVersion7();

        (await hr.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nurse.PostAsJsonAsync($"{Url}/{id}/confirm", new { version = 0 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nurse.PostAsJsonAsync($"{Url}/{id}/cancel", new { reason = "x", version = 0 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

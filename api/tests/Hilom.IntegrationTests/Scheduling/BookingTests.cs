using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Scheduling;

[Collection(ApiTestGroup.Name)]
public sealed class BookingTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    [Fact]
    public async Task Front_desk_books_a_free_slot_and_it_stops_being_free()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var slots = await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician);
        slots.Length.ShouldBe(2);

        var response = await BookingFixture.BookAsync(fixture.FrontDesk, IdOf(slots[0]), patient);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var booked = await response.ReadJsonAsync();
        booked.GetProperty("status").GetString().ShouldBe("requested");
        booked.GetProperty("physician").GetProperty("id").GetString().ShouldBe(physician);
        booked.GetProperty("patient").GetProperty("id").GetString().ShouldBe(patient);
        (await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician)).Select(IdOf).ShouldBe([IdOf(slots[1])]);

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Scheduling.AppointmentBooked.Key));
        var entry = audit.Single();
        entry.ActingForPatientLinkId.ShouldBeNull();
        JsonDocument.Parse(entry.Details!).RootElement.GetProperty("forLinkedPatient").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_slot_holds_one_live_appointment()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        (await BookingFixture.BookAsync(fixture.FrontDesk, slot, await fixture.PatientAsync())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await BookingFixture.BookAsync(fixture.FrontDesk, slot, await fixture.PatientAsync());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ProblemCodeAsync()).ShouldBe("slot_taken");
    }

    [Fact]
    public async Task The_database_also_refuses_a_second_live_appointment_on_a_slot()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        var booked = await (await BookingFixture.BookAsync(fixture.FrontDesk, slot, await fixture.PatientAsync())).ReadJsonAsync();
        var other = await fixture.PatientAsync();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        async Task InsertAsync(string status)
        {
            await using var command = new NpgsqlCommand(
                """
                insert into scheduling.appointments (id, slot_id, patient_id, booked_by, reason, status, created_at, updated_at)
                select gen_random_uuid(), @slot, @patient, booked_by, 'x', @status, now(), now()
                from scheduling.appointments where id = @existing
                """,
                connection);
            command.Parameters.AddWithValue("slot", Guid.Parse(slot));
            command.Parameters.AddWithValue("patient", Guid.Parse(other));
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue("existing", Guid.Parse(IdOf(booked)));
            await command.ExecuteNonQueryAsync(Ct);
        }

        var error = await Should.ThrowAsync<PostgresException>(() => InsertAsync("confirmed"));
        error.ConstraintName.ShouldBe("ux_appointments_one_live_per_slot");

        // A cancelled or declined appointment doesn't hold the slot.
        await InsertAsync("cancelled");
    }

    [Fact]
    public async Task A_portal_user_books_for_a_linked_patient_when_the_link_allows_booking()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var (portal, userId, linkId) = await fixture.LinkedPortalUserAsync(patient, canBook: true);
        var slots = await BookingFixture.OpenSlotsAsync(portal, physician);

        var response = await BookingFixture.BookAsync(portal, IdOf(slots[0]), patient);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var mine = await portal.GetFromJsonAsync<JsonElement>("/api/scheduling/appointments/mine", Ct);
        var appointment = mine.EnumerateArray().Single();
        appointment.GetProperty("patient").GetProperty("id").GetString().ShouldBe(patient);
        appointment.GetProperty("reason").GetString().ShouldBe("Chest pain when climbing stairs");

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == userId && e.Event == AuditEvents.Scheduling.AppointmentBooked.Key));
        audit.Single().ActingForPatientLinkId.ShouldBe(Guid.Parse(linkId));
    }

    [Fact]
    public async Task A_portal_user_cannot_book_without_can_book_or_for_an_unlinked_patient()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var stranger = await fixture.PatientAsync();
        var (portal, userId, _) = await fixture.LinkedPortalUserAsync(patient, canBook: false, canViewRecords: true);
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(portal, physician))[0]);

        var withoutFlag = await BookingFixture.BookAsync(portal, slot, patient);
        var unlinked = await BookingFixture.BookAsync(portal, slot, stranger);

        withoutFlag.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await withoutFlag.ProblemCodeAsync()).ShouldBe("patient_not_found");
        unlinked.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == userId && e.Event == AuditEvents.Access.Denied.Key));
        denied.Count.ShouldBe(2);
        (await BookingFixture.OpenSlotsAsync(portal, physician)).Length.ShouldBe(2);
    }

    [Fact]
    public async Task Closed_and_past_slots_cannot_be_booked()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        (await fixture.FrontDesk.PostAsync($"/api/scheduling/slots/{slot}/close", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await BookingFixture.BookAsync(fixture.FrontDesk, slot, patient);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("slot_not_bookable");
        (await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Free_slots_can_be_filtered_by_specialization()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (cardiologist, cardiology) = await fixture.PhysicianWithSlotsAsync();
        var (pediatrician, pediatrics) = await fixture.PhysicianWithSlotsAsync();

        var cardiologySlots = await fixture.FrontDesk.GetFromJsonAsync<JsonElement>(
            $"/api/scheduling/booking/slots?from={Workforce.StaffFixture.Day(BookingFixture.NextMonday())}&specializationId={cardiology}",
            Ct);
        var pediatricians = await fixture.FrontDesk.GetFromJsonAsync<JsonElement>($"/api/scheduling/booking/physicians?specializationId={pediatrics}", Ct);

        cardiologySlots.EnumerateArray().Select(s => s.GetProperty("physician").GetProperty("id").GetString()).Distinct().ShouldBe([cardiologist]);
        pediatricians.EnumerateArray().Select(IdOf).ShouldBe([pediatrician]);
    }

    [Fact]
    public async Task A_physician_can_open_the_records_of_patients_booked_with_them()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var (colleague, _) = await fixture.PhysicianWithSlotsAsync();
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var mine = await fixture.PatientAsync();
        var theirs = await fixture.PatientAsync();

        (await doctor.GetAsync($"/api/patients/{mine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await BookingFixture.BookAsync(fixture.FrontDesk, IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]), mine);
        await BookingFixture.BookAsync(fixture.FrontDesk, IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, colleague))[0]), theirs);

        (await doctor.GetAsync($"/api/patients/{mine}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await doctor.GetAsync($"/api/patients/{theirs}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var list = await doctor.GetFromJsonAsync<JsonElement>("/api/patients", Ct);
        list.GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([mine]);
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);

        var response = await BookingFixture.BookAsync(fixture.FrontDesk, slot, await fixture.PatientAsync(), reason: " ");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").GetProperty("reason")[0].GetString()
            .ShouldBe("Say briefly why the patient wants to be seen.");
    }

    [Fact]
    public async Task Staff_without_the_permissions_get_403()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");
        var (doctor, _) = await factory.SignInWithRolesAsync("Physician");

        (await hr.GetAsync("/api/scheduling/booking/slots", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nurse.PostAsJsonAsync("/api/scheduling/appointments", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await doctor.PostAsJsonAsync("/api/scheduling/appointments", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

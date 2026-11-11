using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Npgsql;

namespace Hilom.IntegrationTests.Clinical;

/// <summary>Builds encounters through the API: a confirmed appointment due now, checked in by the front desk.</summary>
internal static class EncounterFixture
{
    private const string Url = "/api/clinical/encounters";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    private static uint VersionOf(JsonElement element) => element.GetProperty("version").GetUInt32();

    /// <summary>A physician with slots and a patient whose appointment the front desk confirmed, next Monday at 09:00.</summary>
    public static async Task<(BookingFixture Fixture, string PhysicianId, string PatientId, JsonElement Appointment)> ConfirmedAsync(HilomApiFactory factory)
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var slot = IdOf((await BookingFixture.OpenSlotsAsync(fixture.FrontDesk, physician))[0]);
        var booked = await (await BookingFixture.BookAsync(fixture.FrontDesk, slot, patient, "Chest pain when climbing stairs")).ReadJsonAsync();
        var confirmed = await fixture.FrontDesk.PostAsJsonAsync(
            $"/api/scheduling/appointments/{IdOf(booked)}/confirm",
            new { version = VersionOf(booked) },
            Ct);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (fixture, physician, patient, await confirmed.ReadJsonAsync());
    }

    /// <summary>A confirmed appointment that starts now, so it's today in hospital time.</summary>
    public static async Task<(BookingFixture Fixture, string PhysicianId, string PatientId, JsonElement Appointment)> DueNowAsync(HilomApiFactory factory)
    {
        var (fixture, physician, patient, appointment) = await ConfirmedAsync(factory);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            update scheduling.availability_slots
            set during = tstzrange(now(), now() + interval '30 minutes')
            where id = (select slot_id from scheduling.appointments where id = @id)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.Parse(IdOf(appointment)));
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        return (fixture, physician, patient, appointment);
    }

    public static Task<HttpResponseMessage> CheckInAsync(HttpClient client, JsonElement appointment) =>
        client.PostAsJsonAsync($"{Url}/check-in", new { appointmentId = IdOf(appointment), version = VersionOf(appointment) }, Ct);

    /// <summary>Checked in by the front desk; returns the encounter id.</summary>
    public static async Task<string> CheckedInAsync(BookingFixture fixture, JsonElement appointment)
    {
        var response = await CheckInAsync(fixture.FrontDesk, appointment);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.ReadJsonAsync()).GetProperty("encounterId").GetString()!;
    }
}

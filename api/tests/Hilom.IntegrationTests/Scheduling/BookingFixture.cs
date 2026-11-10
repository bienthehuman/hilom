using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Workforce;

namespace Hilom.IntegrationTests.Scheduling;

/// <summary>Builds what booking tests need, through the API: a physician with slots, patients, linked portal users.</summary>
internal sealed class BookingFixture(HilomApiFactory factory, StaffFixture staff, HttpClient frontDesk)
{
    private static readonly string[] Mondays = ["monday"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient FrontDesk => frontDesk;

    public StaffFixture Staff => staff;

    public static async Task<BookingFixture> CreateAsync(HilomApiFactory factory) =>
        new(factory, await StaffFixture.CreateAsync(factory), (await factory.SignInWithRolesAsync("Front Desk")).Client);

    /// <summary>A Monday at least a week ahead, so every slot is in the future.</summary>
    public static DateOnly NextMonday()
    {
        var day = StaffFixture.Today.AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    public async Task<string> LookupAsync(string list) =>
        (await (await staff.Admin.PostAsJsonAsync($"/api/workforce/{list}", new { name = $"{list} {StaffFixture.Marker()}" }, Ct))
            .ReadJsonAsync()).GetProperty("id").GetString()!;

    /// <summary>A physician taking appointments, with two 30-minute slots on Monday at 09:00 and 09:30.</summary>
    public async Task<(string PhysicianId, string SpecializationId)> PhysicianWithSlotsAsync(string? specializationId = null)
    {
        specializationId ??= await LookupAsync("specializations");
        var response = await staff.Admin.PostAsJsonAsync(
            "/api/workforce/employees",
            new
            {
                firstName = "Jose",
                lastName = $"Santos {StaffFixture.Marker()}",
                sex = "male",
                birthDate = "1980-02-14",
                departmentId = await LookupAsync("departments"),
                positionId = await LookupAsync("positions"),
                professionalType = "physician",
                licenseNumber = "0123456",
                employmentType = "full_time",
                hiredOn = "2015-07-01",
                physician = new { specializationId, acceptsAppointments = true, consultationMinutes = 30 },
            },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var physicianId = (await response.ReadJsonAsync()).GetProperty("id").GetString()!;

        var monday = NextMonday();
        var publish = await frontDesk.PostAsJsonAsync(
            "/api/scheduling/slots/publish",
            new { physicianId, from = StaffFixture.Day(monday), to = StaffFixture.Day(monday), days = Mondays, startTime = "09:00", endTime = "10:00" },
            Ct);
        publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync(Ct));
        return (physicianId, specializationId);
    }

    /// <summary>The physician's free slots next week, soonest first.</summary>
    public static async Task<JsonElement[]> OpenSlotsAsync(HttpClient client, string physicianId) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(
                $"/api/scheduling/booking/slots?from={StaffFixture.Day(NextMonday())}&days=7&physicianId={physicianId}", Ct))
            .EnumerateArray()];

    public async Task<string> PatientAsync()
    {
        var response = await frontDesk.PostAsJsonAsync(
            "/api/patients",
            new
            {
                firstName = "Juan",
                lastName = $"Dela Cruz {StaffFixture.Marker()}",
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
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    /// <summary>A self-registered portal user, linked to the patient by the front desk with the given access.</summary>
    public async Task<(HttpClient Client, Guid UserId, string LinkId)> LinkedPortalUserAsync(string patientId, bool canBook, bool canViewRecords = false)
    {
        var portal = factory.CreateBrowserClient();
        await portal.PrimeAntiforgeryAsync();
        var registered = await portal.PostAsJsonAsync(
            "/api/access/register",
            new { displayName = "Maria Dela Cruz", email = $"maria.{Guid.NewGuid():N}@family.test", password = "Portal-Password-2026" },
            Ct);
        registered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var userId = Guid.Parse((await registered.ReadJsonAsync()).GetProperty("id").GetString()!);

        var request = await portal.PostAsJsonAsync(
            "/api/patients/links",
            new { patientName = "Juan Dela Cruz", patientBirthDate = "1958-03-21", relationship = "child" },
            Ct);
        var linkId = (await request.ReadJsonAsync()).GetProperty("id").GetString()!;
        var queued = (await frontDesk.GetFromJsonAsync<JsonElement>("/api/patients/links?status=pending&pageSize=100", Ct))
            .GetProperty("items").EnumerateArray().Single(l => l.GetProperty("id").GetString() == linkId);
        var verify = await frontDesk.PostAsJsonAsync(
            $"/api/patients/links/{linkId}/verify",
            new { patientId, relationship = "child", canBook, canViewRecords, version = queued.GetProperty("version").GetUInt32() },
            Ct);
        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (portal, userId, linkId);
    }

    public static Task<HttpResponseMessage> BookAsync(HttpClient client, string slotId, string patientId, string reason = "Chest pain when climbing stairs") =>
        client.PostAsJsonAsync("/api/scheduling/appointments", new { slotId, patientId, reason }, Ct);
}

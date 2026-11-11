using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Clinical;

[Collection(ApiTestGroup.Name)]
public sealed class PhysicianOrdersTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(string encounterId) => $"/api/clinical/encounters/{encounterId}/orders";

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    private static readonly object Amoxicillin = new
    {
        orderType = "medication",
        details = "For the chest infection; with food",
        medicationName = "Amoxicillin",
        dose = "500 mg",
        route = "oral",
        frequency = "every 8 hours",
    };

    /// <summary>An open clinic visit and its attending physician, signed in.</summary>
    private async Task<(BookingFixture Fixture, string EncounterId, string PatientId, HttpClient Doctor)> VisitAsync()
    {
        var (fixture, physician, patient, appointment) = await EncounterFixture.DueNowAsync(factory);
        var encounterId = await EncounterFixture.CheckedInAsync(fixture, appointment);
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        return (fixture, encounterId, patient, doctor);
    }

    private static async Task<JsonElement> OrderAsync(HttpClient client, string encounterId, object? order = null)
    {
        var response = await client.PostAsJsonAsync(Url(encounterId), order ?? Amoxicillin, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private async Task<PostgresException> ExecuteFailingAsync(string sql, string orderId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.Parse(orderId));
        return await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
    }

    [Fact]
    public async Task The_attending_physician_orders_a_medication_and_the_order_text_stays_out_of_the_audit_log()
    {
        var (_, encounterId, patient, doctor) = await VisitAsync();

        var order = await OrderAsync(doctor, encounterId);

        order.GetProperty("orderType").GetString().ShouldBe("medication");
        order.GetProperty("status").GetString().ShouldBe("active");
        order.GetProperty("medicationName").GetString().ShouldBe("Amoxicillin");
        order.GetProperty("dose").GetString().ShouldBe("500 mg");
        order.GetProperty("orderedBy").GetString().ShouldStartWith("Jose Santos");
        order.GetProperty("canDiscontinue").GetBoolean().ShouldBeTrue();
        order.GetProperty("discontinued").ValueKind.ShouldBe(JsonValueKind.Null);
        var list = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).EnumerateArray().ToList();
        list.ShouldHaveSingleItem().GetProperty("details").GetString()!.ShouldContain("chest infection");
        (await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct)).GetProperty("canWriteOrders").GetBoolean().ShouldBeTrue();

        var created = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.OrderCreated.Key)))
            .ShouldHaveSingleItem();
        created.Severity.ShouldBe(AuditSeverity.Notice);
        created.Details!.ShouldContain("Amoxicillin");
        var all = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient)));
        all.ShouldAllBe(e => (e.Changes == null || !e.Changes.Contains("chest infection")) && (e.Details == null || !e.Details.Contains("chest infection")));
    }

    [Fact]
    public async Task Other_kinds_of_order_need_no_medication_fields_and_drop_any_that_are_sent()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();

        var lab = await OrderAsync(doctor, encounterId, new { orderType = "laboratory", details = "Full blood count", dose = "ignored" });
        var diet = await OrderAsync(doctor, encounterId, new { orderType = "diet", details = "Soft diet, no salt added" });

        lab.GetProperty("dose").ValueKind.ShouldBe(JsonValueKind.Null);
        lab.GetProperty("medicationName").ValueKind.ShouldBe(JsonValueKind.Null);
        diet.GetProperty("orderType").GetString().ShouldBe("diet");
        (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_medication_order_needs_name_dose_route_and_frequency()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();

        var none = await doctor.PostAsJsonAsync(Url(encounterId), new { orderType = "medication", details = "Antibiotic" }, Ct);
        var blank = await doctor.PostAsJsonAsync(
            Url(encounterId),
            new { orderType = "medication", details = "Antibiotic", medicationName = "Amoxicillin", dose = "  ", route = "oral", frequency = "" },
            Ct);
        var noDetails = await doctor.PostAsJsonAsync(Url(encounterId), new { orderType = "diet", details = " " }, Ct);
        var unknown = await doctor.PostAsJsonAsync(Url(encounterId), new { orderType = "prayer", details = "Amen" }, Ct);

        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await none.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("medicationName")[0].GetString().ShouldBe("Name the medication.");
        errors.GetProperty("dose").GetArrayLength().ShouldBe(1);
        errors.GetProperty("route").GetArrayLength().ShouldBe(1);
        errors.GetProperty("frequency").GetArrayLength().ShouldBe(1);
        var blankErrors = (await blank.ReadJsonAsync()).GetProperty("errors");
        blankErrors.TryGetProperty("dose", out _).ShouldBeTrue();
        blankErrors.TryGetProperty("frequency", out _).ShouldBeTrue();
        blankErrors.TryGetProperty("route", out _).ShouldBeFalse();
        (await noDetails.ReadJsonAsync()).GetProperty("errors").GetProperty("details")[0].GetString().ShouldBe("Write the order.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_database_refuses_a_medication_order_without_its_fields_even_around_the_api()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();
        var order = await OrderAsync(doctor, encounterId);

        var noDose = await ExecuteFailingAsync("update clinical.physician_orders set dose = null where id = @id", IdOf(order));
        var blankRoute = await ExecuteFailingAsync("update clinical.physician_orders set route = '   ' where id = @id", IdOf(order));
        var noName = await ExecuteFailingAsync("update clinical.physician_orders set medication_name = null, frequency = null where id = @id", IdOf(order));
        var noReason = await ExecuteFailingAsync(
            "update clinical.physician_orders set status = 'discontinued', discontinued_at = now() where id = @id", IdOf(order));

        noDose.ConstraintName.ShouldBe("ck_physician_orders_medication_fields");
        blankRoute.ConstraintName.ShouldBe("ck_physician_orders_medication_fields");
        noName.ConstraintName.ShouldBe("ck_physician_orders_medication_fields");
        noReason.ConstraintName.ShouldBe("ck_physician_orders_discontinue");
    }

    [Fact]
    public async Task Discontinuing_needs_a_reason_and_the_order_stays_in_the_list()
    {
        var (_, encounterId, patient, doctor) = await VisitAsync();
        var order = await OrderAsync(doctor, encounterId);
        var url = $"{Url(encounterId)}/{IdOf(order)}/discontinue";
        var version = order.GetProperty("version").GetUInt32();

        var noReason = await doctor.PostAsJsonAsync(url, new { reason = "   ", version }, Ct);
        var stale = await doctor.PostAsJsonAsync(url, new { reason = "Rash after the first dose", version = version + 1000 }, Ct);
        var stopped = await doctor.PostAsJsonAsync(url, new { reason = "Rash after the first dose", version }, Ct);
        var again = await doctor.PostAsJsonAsync(url, new { reason = "Again", version }, Ct);

        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noReason.ReadJsonAsync()).GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("Say why the order is being discontinued.");
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stopped.StatusCode.ShouldBe(HttpStatusCode.OK, await stopped.Content.ReadAsStringAsync(Ct));
        var body = await stopped.ReadJsonAsync();
        body.GetProperty("status").GetString().ShouldBe("discontinued");
        body.GetProperty("discontinued").GetProperty("reason").GetString().ShouldBe("Rash after the first dose");
        body.GetProperty("canDiscontinue").GetBoolean().ShouldBeFalse();
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("order_not_active");

        var list = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).EnumerateArray().ToList();
        list.ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("discontinued");
        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.OrderDiscontinued.Key)))
            .ShouldHaveSingleItem();
        entry.Details!.ShouldContain("Rash after the first dose");
        entry.Details!.ShouldNotContain("chest infection");
    }

    [Fact]
    public async Task Orders_follow_the_encounter_scope_and_only_physicians_write_them()
    {
        var (fixture, encounterId, _, doctor) = await VisitAsync();
        var order = await OrderAsync(doctor, encounterId);
        var (colleagueId, _) = await fixture.PhysicianWithSlotsAsync();
        var colleague = await fixture.Staff.SignInAsEmployeeAsync(colleagueId, "Physician");
        var chiefNurse = await fixture.Staff.SignInAsEmployeeAsync(await fixture.Staff.NurseAsync("Villanueva"), "Chief Nurse");
        var discontinue = $"{Url(encounterId)}/{IdOf(order)}/discontinue";
        var reason = new { reason = "Not needed", version = order.GetProperty("version").GetUInt32() };

        // Out of scope is a 404, whoever else is a physician.
        (await colleague.PostAsJsonAsync(Url(encounterId), Amoxicillin, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.GetAsync(Url(encounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.PostAsJsonAsync(discontinue, reason, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // No permission is a 403: nurses see orders but don't write or stop them; the front desk sees none.
        (await chiefNurse.PostAsJsonAsync(Url(encounterId), Amoxicillin, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await chiefNurse.PostAsJsonAsync(discontinue, reason, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.FrontDesk.GetAsync(Url(encounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // An administrator may order everywhere but isn't a physician, so can't sign an order.
        var admin = await fixture.Staff.Admin.PostAsJsonAsync(Url(encounterId), Amoxicillin, Ct);
        admin.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.ProblemCodeAsync()).ShouldBe("not_a_physician");

        // ...but can stop one, as the user.
        var stopped = await fixture.Staff.Admin.PostAsJsonAsync(discontinue, reason, Ct);
        stopped.StatusCode.ShouldBe(HttpStatusCode.OK, await stopped.Content.ReadAsStringAsync(Ct));
        (await stopped.ReadJsonAsync()).GetProperty("discontinued").GetProperty("by").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_closed_encounter_takes_no_new_orders()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();
        var encounter = await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct);
        (await doctor.PostAsJsonAsync($"/api/clinical/encounters/{encounterId}/close", new { version = encounter.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var write = await doctor.PostAsJsonAsync(Url(encounterId), Amoxicillin, Ct);

        write.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await write.ProblemCodeAsync()).ShouldBe("encounter_not_open");
        (await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct)).GetProperty("canWriteOrders").GetBoolean().ShouldBeFalse();
    }
}

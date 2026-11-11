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
public sealed class MedicationAdministrationTests(HilomApiFactory factory)
{
    private const string Encounters = "/api/clinical/encounters";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(string encounterId) => $"{Encounters}/{encounterId}/medication-administrations";

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

    private sealed record Stay(BookingFixture Fixture, string WardId, string PatientId, string EncounterId, string OrderId, HttpClient Doctor, HttpClient Nurse);

    /// <summary>A patient admitted to a ward, an active amoxicillin order, and a staff nurse on the ward today.</summary>
    private async Task<Stay> StayAsync()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var ward = await fixture.Staff.WardAsync();
        var bed = await fixture.Staff.Admin.PostAsJsonAsync($"/api/facility/wards/{ward}/beds", new { label = "Bed 1" }, Ct);
        bed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var admitted = await fixture.FrontDesk.PostAsJsonAsync(
            "/api/clinical/admissions",
            new { patientId = patient, attendingPhysicianId = physician, bedId = IdOf(await bed.ReadJsonAsync()), admittingDiagnosis = "Pneumonia", reason = "IV antibiotics" },
            Ct);
        admitted.StatusCode.ShouldBe(HttpStatusCode.Created, await admitted.Content.ReadAsStringAsync(Ct));
        var encounterId = (await admitted.ReadJsonAsync()).GetProperty("encounterId").GetString()!;
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        var order = await OrderAsync(doctor, encounterId, Amoxicillin);
        var nurseId = await fixture.Staff.NurseAsync();
        await fixture.Staff.AssignAsync(nurseId, ward, StaffFixture.Today);
        var nurse = await fixture.Staff.SignInAsEmployeeAsync(nurseId, "Staff Nurse");
        return new Stay(fixture, ward, patient, encounterId, order, doctor, nurse);
    }

    private static async Task<string> OrderAsync(HttpClient doctor, string encounterId, object order)
    {
        var response = await doctor.PostAsJsonAsync($"{Encounters}/{encounterId}/orders", order, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return IdOf(await response.ReadJsonAsync());
    }

    private static Task<HttpResponseMessage> RecordAsync(HttpClient client, string encounterId, object dose) =>
        client.PostAsJsonAsync(Url(encounterId), dose, Ct);

    private static async Task<JsonElement> RecordedAsync(HttpClient client, string encounterId, object dose)
    {
        var response = await RecordAsync(client, encounterId, dose);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static object Given(string orderId) => new { orderId, outcome = "given", doseGiven = "500 mg" };

    private static async Task<JsonElement> MarOrderAsync(HttpClient client, string encounterId, string orderId) =>
        (await client.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).EnumerateArray().Single(o => o.GetProperty("orderId").GetString() == orderId);

    private static async Task AllergyAsync(HttpClient client, string patientId, string allergen)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/allergies",
            new { allergen, category = "drug", severity = "severe", reaction = "Hives" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<PostgresException> ExecuteFailingAsync(string sql, string id)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.Parse(id));
        return await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
    }

    [Fact]
    public async Task A_nurse_on_the_ward_records_a_dose_given_against_the_order()
    {
        var s = await StayAsync();

        var dose = await RecordedAsync(s.Nurse, s.EncounterId, Given(s.OrderId));

        dose.GetProperty("outcome").GetString().ShouldBe("given");
        dose.GetProperty("doseGiven").GetString().ShouldBe("500 mg");
        dose.GetProperty("orderId").GetString().ShouldBe(s.OrderId);
        dose.GetProperty("administeredBy").GetString().ShouldStartWith("Ana Reyes");
        dose.GetProperty("canVoid").GetBoolean().ShouldBeTrue();
        dose.GetProperty("voided").ValueKind.ShouldBe(JsonValueKind.Null);
        var order = await MarOrderAsync(s.Nurse, s.EncounterId, s.OrderId);
        order.GetProperty("medicationName").GetString().ShouldBe("Amoxicillin");
        order.GetProperty("dose").GetString().ShouldBe("500 mg");
        order.GetProperty("canRecord").GetBoolean().ShouldBeTrue();
        order.GetProperty("allergyMatches").GetArrayLength().ShouldBe(0);
        order.GetProperty("administrations").EnumerateArray().Select(IdOf).ShouldBe([IdOf(dose)]);
        (await s.Nurse.GetFromJsonAsync<JsonElement>($"{Encounters}/{s.EncounterId}", Ct)).GetProperty("canAdminister").GetBoolean().ShouldBeTrue();

        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId) && e.Event == AuditEvents.Clinical.MedicationAdministered.Key)))
            .ShouldHaveSingleItem();
        entry.Severity.ShouldBe(AuditSeverity.Notice);
        entry.Details!.ShouldContain("Amoxicillin");
        entry.Details!.ShouldContain("given");
    }

    [Fact]
    public async Task A_dose_not_given_needs_a_reason_and_the_reason_stays_out_of_the_audit_log()
    {
        var s = await StayAsync();

        var noReason = await RecordAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "held", notes = "  " });
        var noDose = await RecordAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "given" });
        var unknown = await RecordAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "spilled", notes = "Oops" });
        var held = await RecordedAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "held", doseGiven = "500 mg", notes = "Vomiting since morning" });
        var refused = await RecordedAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "refused", notes = "Says it upsets his stomach" });
        var missing = await RecordedAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "not_available", notes = "Pharmacy out of stock" });

        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noReason.ReadJsonAsync()).GetProperty("errors").GetProperty("notes")[0].GetString().ShouldBe("Say why the dose wasn't given.");
        noDose.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noDose.ReadJsonAsync()).GetProperty("errors").GetProperty("doseGiven")[0].GetString().ShouldBe("Give the dose given, e.g. 500 mg.");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        held.GetProperty("outcome").GetString().ShouldBe("held");
        held.GetProperty("doseGiven").ValueKind.ShouldBe(JsonValueKind.Null);
        held.GetProperty("notes").GetString().ShouldBe("Vomiting since morning");
        refused.GetProperty("outcome").GetString().ShouldBe("refused");
        missing.GetProperty("outcome").GetString().ShouldBe("not_available");
        (await MarOrderAsync(s.Nurse, s.EncounterId, s.OrderId)).GetProperty("administrations").GetArrayLength().ShouldBe(3);

        var all = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId)));
        all.ShouldAllBe(e => (e.Changes == null || !e.Changes.Contains("Vomiting")) && (e.Details == null || !e.Details.Contains("Vomiting")));
    }

    [Fact]
    public async Task The_database_refuses_a_dose_without_its_amount_or_a_missed_dose_without_a_reason()
    {
        var s = await StayAsync();
        var given = await RecordedAsync(s.Nurse, s.EncounterId, Given(s.OrderId));
        var held = await RecordedAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "held", notes = "BP 80/50" });

        var noDose = await ExecuteFailingAsync("update clinical.medication_administrations set dose_given = ' ' where id = @id", IdOf(given));
        var heldWithDose = await ExecuteFailingAsync("update clinical.medication_administrations set dose_given = '500 mg' where id = @id", IdOf(held));
        var noReason = await ExecuteFailingAsync("update clinical.medication_administrations set notes = null where id = @id", IdOf(held));
        var voidNoReason = await ExecuteFailingAsync("update clinical.medication_administrations set voided_at = now() where id = @id", IdOf(given));

        noDose.ConstraintName.ShouldBe("ck_medication_administrations_dose_given");
        heldWithDose.ConstraintName.ShouldBe("ck_medication_administrations_dose_given");
        noReason.ConstraintName.ShouldBe("ck_medication_administrations_reason");
        voidNoReason.ConstraintName.ShouldBe("ck_medication_administrations_void");
    }

    [Fact]
    public async Task With_active_allergies_the_nurse_confirms_the_check_and_name_matches_are_flagged()
    {
        var s = await StayAsync();
        await AllergyAsync(s.Fixture.Staff.Admin, s.PatientId, "amoxicillin");
        await AllergyAsync(s.Fixture.Staff.Admin, s.PatientId, "Latex");

        var order = await MarOrderAsync(s.Nurse, s.EncounterId, s.OrderId);
        var unchecked_ = await RecordAsync(s.Nurse, s.EncounterId, Given(s.OrderId));
        var checked_ = await RecordAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "held", notes = "Allergy: asked the doctor to review", allergiesChecked = true });

        order.GetProperty("allergyMatches").EnumerateArray().Select(a => a.GetString()).ShouldBe(["amoxicillin"]);
        unchecked_.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await unchecked_.ProblemCodeAsync()).ShouldBe("allergy_check_required");
        checked_.StatusCode.ShouldBe(HttpStatusCode.Created, await checked_.Content.ReadAsStringAsync(Ct));
        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId) && e.Event == AuditEvents.Clinical.MedicationAdministered.Key)))
            .ShouldHaveSingleItem();
        entry.Details!.ShouldContain("amoxicillin");
        entry.Details!.ShouldNotContain("Latex");
    }

    [Fact]
    public async Task Doses_go_only_against_active_medication_orders_on_the_encounter()
    {
        var s = await StayAsync();
        var lab = await OrderAsync(s.Doctor, s.EncounterId, new { orderType = "laboratory", details = "Full blood count" });
        var stopped = await OrderAsync(s.Doctor, s.EncounterId, new { orderType = "medication", details = "For fever", medicationName = "Paracetamol", dose = "500 mg", route = "oral", frequency = "every 6 hours as needed" });
        var stoppedVersion = (await s.Doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{s.EncounterId}/orders", Ct))
            .EnumerateArray().Single(o => IdOf(o) == stopped).GetProperty("version").GetUInt32();
        (await s.Doctor.PostAsJsonAsync($"{Encounters}/{s.EncounterId}/orders/{stopped}/discontinue", new { reason = "Fever settled", version = stoppedVersion }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var toLab = await RecordAsync(s.Nurse, s.EncounterId, Given(lab));
        var toStopped = await RecordAsync(s.Nurse, s.EncounterId, Given(stopped));
        var toUnknown = await RecordAsync(s.Nurse, s.EncounterId, Given(Guid.CreateVersion7().ToString()));

        toLab.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await toLab.ProblemCodeAsync()).ShouldBe("not_a_medication_order");
        toStopped.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await toStopped.ProblemCodeAsync()).ShouldBe("order_not_active");
        toUnknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await toUnknown.ProblemCodeAsync()).ShouldBe("order_not_found");

        // The MAR lists medication orders only, active ones first; a stopped one stays, without recording.
        var mar = (await s.Nurse.GetFromJsonAsync<JsonElement>(Url(s.EncounterId), Ct)).EnumerateArray().ToList();
        mar.Select(o => o.GetProperty("orderId").GetString()).ShouldBe([s.OrderId, stopped]);
        mar[1].GetProperty("status").GetString().ShouldBe("discontinued");
        mar[1].GetProperty("canRecord").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_time_given_is_not_in_the_future_or_before_the_order()
    {
        var s = await StayAsync();
        var orderedAt = (await MarOrderAsync(s.Nurse, s.EncounterId, s.OrderId)).GetProperty("orderedAt").GetDateTimeOffset();

        var future = await RecordAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "given", doseGiven = "500 mg", administeredAt = DateTimeOffset.UtcNow.AddHours(1) });
        var early = await RecordAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "given", doseGiven = "500 mg", administeredAt = DateTimeOffset.UtcNow.AddDays(-1) });
        var earlier = await RecordedAsync(s.Nurse, s.EncounterId, new { orderId = s.OrderId, outcome = "given", doseGiven = "500 mg", administeredAt = orderedAt.AddMilliseconds(1) });

        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await future.ProblemCodeAsync()).ShouldBe("invalid_administered_at");
        (await early.ReadJsonAsync()).GetProperty("detail").GetString().ShouldBe("The time can't be before the order was written.");
        earlier.GetProperty("administeredAt").GetDateTimeOffset().ShouldBe(orderedAt.AddMilliseconds(1));
    }

    [Fact]
    public async Task Recording_follows_the_ward_scope_and_physicians_only_read_the_mar()
    {
        var s = await StayAsync();
        var otherWard = await s.Fixture.Staff.WardAsync();
        var elsewhereId = await s.Fixture.Staff.NurseAsync("Cruz");
        await s.Fixture.Staff.AssignAsync(elsewhereId, otherWard, StaffFixture.Today);
        var elsewhere = await s.Fixture.Staff.SignInAsEmployeeAsync(elsewhereId, "Staff Nurse");
        var chiefNurse = await s.Fixture.Staff.SignInAsEmployeeAsync(await s.Fixture.Staff.NurseAsync("Villanueva"), "Chief Nurse");

        // Out of scope is a 404, logged.
        var outside = await RecordAsync(elsewhere, s.EncounterId, Given(s.OrderId));
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await elsewhere.GetAsync(Url(s.EncounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(s.EncounterId) && e.Event == AuditEvents.Access.Denied.Key)))
            .ShouldNotBeEmpty();

        // No permission is a 403: the physician reads the MAR but doesn't record on it; the front desk doesn't see it.
        (await RecordAsync(s.Doctor, s.EncounterId, Given(s.OrderId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Doctor.GetAsync(Url(s.EncounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{s.EncounterId}", Ct)).GetProperty("canAdminister").GetBoolean().ShouldBeFalse();
        (await s.Fixture.FrontDesk.GetAsync(Url(s.EncounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The chief nurse records on every ward. An administrator has the scope but no employee record to sign with.
        await RecordedAsync(chiefNurse, s.EncounterId, Given(s.OrderId));
        var admin = await RecordAsync(s.Fixture.Staff.Admin, s.EncounterId, Given(s.OrderId));
        admin.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.ProblemCodeAsync()).ShouldBe("no_employee_record");
    }

    [Fact]
    public async Task A_dose_entered_in_error_is_voided_with_a_reason_and_stays_on_the_record()
    {
        var s = await StayAsync();
        var dose = await RecordedAsync(s.Nurse, s.EncounterId, Given(s.OrderId));
        var colleagueId = await s.Fixture.Staff.NurseAsync("Garcia");
        await s.Fixture.Staff.AssignAsync(colleagueId, s.WardId, StaffFixture.Today);
        var colleague = await s.Fixture.Staff.SignInAsEmployeeAsync(colleagueId, "Staff Nurse");
        var url = $"{Url(s.EncounterId)}/{IdOf(dose)}/void";

        var noReason = await s.Nurse.PostAsJsonAsync(url, new { reason = " " }, Ct);
        var notTheirs = await colleague.PostAsJsonAsync(url, new { reason = "Wrong patient" }, Ct);
        var voided = await s.Nurse.PostAsJsonAsync(url, new { reason = "Recorded on the wrong order" }, Ct);
        var again = await s.Nurse.PostAsJsonAsync(url, new { reason = "Again" }, Ct);

        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        notTheirs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        voided.StatusCode.ShouldBe(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync(Ct));
        var body = await voided.ReadJsonAsync();
        body.GetProperty("voided").GetProperty("reason").GetString().ShouldBe("Recorded on the wrong order");
        body.GetProperty("canVoid").GetBoolean().ShouldBeFalse();
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("record_already_voided");

        var listed = (await MarOrderAsync(colleague, s.EncounterId, s.OrderId)).GetProperty("administrations").EnumerateArray().ToList();
        listed.ShouldHaveSingleItem().GetProperty("voided").ValueKind.ShouldBe(JsonValueKind.Object);
        var entry = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId) && e.Event == AuditEvents.Clinical.RecordVoided.Key)))
            .ShouldHaveSingleItem();
        entry.Severity.ShouldBe(AuditSeverity.Critical);
        entry.Details!.ShouldContain("medication_administration");
        entry.Details!.ShouldContain("Recorded on the wrong order");
    }

    [Fact]
    public async Task A_discharged_stay_takes_no_more_doses_but_its_mar_can_still_be_read_and_voided()
    {
        var s = await StayAsync();
        var dose = await RecordedAsync(s.Nurse, s.EncounterId, Given(s.OrderId));
        var stay = await s.Doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{s.EncounterId}", Ct);
        (await s.Doctor.PostAsJsonAsync(
                $"/api/clinical/admissions/{s.EncounterId}/discharge",
                new { disposition = "home", summary = "Home on oral antibiotics.", version = stay.GetProperty("admission").GetProperty("version").GetUInt32() },
                Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The nurse's ward no longer reaches the stay; the chief nurse (all) still does.
        var chiefNurse = await s.Fixture.Staff.SignInAsEmployeeAsync(await s.Fixture.Staff.NurseAsync("Villanueva"), "Chief Nurse");
        var after = await RecordAsync(chiefNurse, s.EncounterId, Given(s.OrderId));

        after.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await after.ProblemCodeAsync()).ShouldBe("encounter_not_open");
        (await MarOrderAsync(chiefNurse, s.EncounterId, s.OrderId)).GetProperty("canRecord").GetBoolean().ShouldBeFalse();
        (await s.Fixture.Staff.Admin.PostAsJsonAsync($"{Url(s.EncounterId)}/{IdOf(dose)}/void", new { reason = "Wrong patient" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

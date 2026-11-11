using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Clinical;

[Collection(ApiTestGroup.Name)]
public sealed class VitalSignsTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(string encounterId) => $"/api/clinical/encounters/{encounterId}/vital-signs";

    private static readonly object FullSet = new
    {
        temperatureC = 38.5m,
        systolicBp = 185,
        diastolicBp = 95,
        heartRate = 88,
        respiratoryRate = 18,
        oxygenSaturation = 97,
        painScore = 4,
        weightKg = 70m,
        heightCm = 175m,
        notes = "Patient anxious, recheck BP in 15 minutes",
    };

    /// <summary>An open clinic visit and its attending physician, signed in.</summary>
    private async Task<(Hilom.IntegrationTests.Scheduling.BookingFixture Fixture, string EncounterId, string PatientId, HttpClient Doctor)> VisitAsync()
    {
        var (fixture, physician, patient, appointment) = await EncounterFixture.DueNowAsync(factory);
        var encounterId = await EncounterFixture.CheckedInAsync(fixture, appointment);
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        return (fixture, encounterId, patient, doctor);
    }

    [Fact]
    public async Task The_attending_physician_records_vitals_and_each_value_is_flagged()
    {
        var (_, encounterId, patient, doctor) = await VisitAsync();

        var response = await doctor.PostAsJsonAsync(Url(encounterId), FullSet, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var recorded = await response.ReadJsonAsync();
        recorded.GetProperty("recordedBy").GetString().ShouldStartWith("Jose Santos");
        recorded.GetProperty("bmi").GetDecimal().ShouldBe(22.9m);
        var flags = recorded.GetProperty("flags");
        flags.GetProperty("temperatureC").GetString().ShouldBe("high");
        flags.GetProperty("systolicBp").GetString().ShouldBe("critical_high");
        flags.GetProperty("diastolicBp").GetString().ShouldBe("high");
        flags.GetProperty("heartRate").GetString().ShouldBe("normal");
        flags.GetProperty("oxygenSaturation").GetString().ShouldBe("normal");

        var second = await doctor.PostAsJsonAsync(Url(encounterId), new { oxygenSaturation = 88, heartRate = 132 }, Ct);
        var secondFlags = (await second.ReadJsonAsync()).GetProperty("flags");
        secondFlags.GetProperty("oxygenSaturation").GetString().ShouldBe("critical_low");
        secondFlags.GetProperty("heartRate").GetString().ShouldBe("critical_high");
        secondFlags.GetProperty("temperatureC").ValueKind.ShouldBe(JsonValueKind.Null);

        var list = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).EnumerateArray().ToList();
        list.Count.ShouldBe(2);
        list[0].GetProperty("oxygenSaturation").GetInt32().ShouldBe(88);

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.VitalsRecorded.Key));
        audit.Count.ShouldBe(2);
        audit.ShouldContain(e => e.Details!.Contains("systolicBp"));
        var dataChanges = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.EntityType == "VitalSigns"));
        dataChanges.ShouldNotBeEmpty();
        dataChanges.ShouldAllBe(e => e.Changes == null || !e.Changes.Contains("anxious"));
    }

    [Fact]
    public async Task Implausible_or_incomplete_sets_are_rejected()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();

        var empty = await doctor.PostAsJsonAsync(Url(encounterId), new { notes = "Nothing taken" }, Ct);
        var halfBp = await doctor.PostAsJsonAsync(Url(encounterId), new { systolicBp = 120 }, Ct);
        var invertedBp = await doctor.PostAsJsonAsync(Url(encounterId), new { systolicBp = 80, diastolicBp = 90 }, Ct);
        var hot = await doctor.PostAsJsonAsync(Url(encounterId), new { temperatureC = 50m }, Ct);

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.ReadJsonAsync()).GetProperty("errors").GetProperty("measurements")[0].GetString().ShouldBe("Enter at least one measurement.");
        (await halfBp.ReadJsonAsync()).GetProperty("errors").GetProperty("diastolicBp")[0].GetString().ShouldBe("Enter both blood pressure numbers.");
        (await invertedBp.ReadJsonAsync()).GetProperty("errors").GetProperty("diastolicBp")[0].GetString().ShouldBe("Diastolic must be lower than systolic.");
        hot.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_time_must_fall_within_the_encounter()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();

        var before = await doctor.PostAsJsonAsync(Url(encounterId), new { heartRate = 80, recordedAt = DateTimeOffset.UtcNow.AddHours(-3) }, Ct);
        var future = await doctor.PostAsJsonAsync(Url(encounterId), new { heartRate = 80, recordedAt = DateTimeOffset.UtcNow.AddHours(1) }, Ct);
        var justNow = await doctor.PostAsJsonAsync(Url(encounterId), new { heartRate = 80, recordedAt = DateTimeOffset.UtcNow }, Ct);

        (await before.ProblemCodeAsync()).ShouldBe("invalid_recorded_at");
        (await future.ProblemCodeAsync()).ShouldBe("invalid_recorded_at");
        justNow.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Vitals_follow_the_encounter_scope_and_need_an_employee_record()
    {
        var (fixture, encounterId, _, doctor) = await VisitAsync();
        var (colleagueId, _) = await fixture.PhysicianWithSlotsAsync();
        var colleague = await fixture.Staff.SignInAsEmployeeAsync(colleagueId, "Physician");
        var chiefNurse = await fixture.Staff.SignInAsEmployeeAsync(await fixture.Staff.NurseAsync("Villanueva"), "Chief Nurse");
        var (staffNurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        var byColleague = await colleague.PostAsJsonAsync(Url(encounterId), new { heartRate = 80 }, Ct);
        var byFrontDesk = await fixture.FrontDesk.PostAsJsonAsync(Url(encounterId), new { heartRate = 80 }, Ct);
        var byStaffNurse = await staffNurse.PostAsJsonAsync(Url(encounterId), new { heartRate = 80 }, Ct);
        var byAdmin = await fixture.Staff.Admin.PostAsJsonAsync(Url(encounterId), new { heartRate = 80 }, Ct);
        var byChiefNurse = await chiefNurse.PostAsJsonAsync(Url(encounterId), new { heartRate = 80 }, Ct);

        byColleague.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        byFrontDesk.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        byStaffNurse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        byAdmin.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await byAdmin.ProblemCodeAsync()).ShouldBe("no_employee_record");
        byChiefNurse.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct)).GetProperty("canRecordVitals").GetBoolean().ShouldBeTrue();
        (await fixture.Staff.Admin.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct)).GetProperty("canRecordVitals").GetBoolean().ShouldBeFalse();
        (await colleague.GetAsync(Url(encounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nothing_is_recorded_on_a_closed_encounter()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();
        var encounter = await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct);
        (await doctor.PostAsJsonAsync($"/api/clinical/encounters/{encounterId}/close", new { version = encounter.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await doctor.PostAsJsonAsync(Url(encounterId), new { heartRate = 80 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("encounter_not_open");
    }

    [Fact]
    public async Task Any_signed_in_user_reads_the_ranges()
    {
        var (nurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        var ranges = await nurse.GetFromJsonAsync<JsonElement>("/api/clinical/vital-ranges", Ct);

        ranges.GetProperty("temperatureC").GetProperty("normalHigh").GetDecimal().ShouldBe(37.9m);
        ranges.GetProperty("oxygenSaturation").GetProperty("criticalHigh").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}

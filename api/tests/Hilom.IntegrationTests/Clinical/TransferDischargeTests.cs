using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Hilom.IntegrationTests.Workforce;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Clinical;

[Collection(ApiTestGroup.Name)]
public sealed class TransferDischargeTests(HilomApiFactory factory)
{
    private const string Admissions = "/api/clinical/admissions";
    private const string Encounters = "/api/clinical/encounters";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    /// <summary>
    /// Two wards (W3 with two beds, W5 with one), a patient admitted to W3 Bed 1, and a head nurse assigned to
    /// each ward today (Head Nurse holds <c>clinical.admissions.transfer</c> at ward scope).
    /// </summary>
    private sealed record Stay(
        BookingFixture Fixture,
        string PhysicianId,
        string PatientId,
        string EncounterId,
        string Ward3,
        string[] Ward3Beds,
        string Ward5,
        string[] Ward5Beds,
        HttpClient Ward3Nurse,
        HttpClient Ward5Nurse);

    private async Task<Stay> AdmittedAsync()
    {
        var fixture = await BookingFixture.CreateAsync(factory);
        var (physician, _) = await fixture.PhysicianWithSlotsAsync();
        var patient = await fixture.PatientAsync();
        var (ward3, ward3Beds) = await WardWithBedsAsync(fixture.Staff.Admin, "1", "2");
        var (ward5, ward5Beds) = await WardWithBedsAsync(fixture.Staff.Admin, "1");
        var admitted = await fixture.FrontDesk.PostAsJsonAsync(
            Admissions,
            new
            {
                patientId = patient,
                attendingPhysicianId = physician,
                bedId = ward3Beds[0],
                admittingDiagnosis = "Community-acquired pneumonia",
                reason = "Needs IV antibiotics and oxygen",
            },
            Ct);
        admitted.StatusCode.ShouldBe(HttpStatusCode.Created, await admitted.Content.ReadAsStringAsync(Ct));
        var encounterId = (await admitted.ReadJsonAsync()).GetProperty("encounterId").GetString()!;

        var santos = await fixture.Staff.NurseAsync("Santos");
        var garcia = await fixture.Staff.NurseAsync("Garcia");
        await fixture.Staff.AssignAsync(santos, ward3, StaffFixture.Today);
        await fixture.Staff.AssignAsync(garcia, ward5, StaffFixture.Today);
        return new Stay(
            fixture,
            physician,
            patient,
            encounterId,
            ward3,
            ward3Beds,
            ward5,
            ward5Beds,
            await fixture.Staff.SignInAsEmployeeAsync(santos, "Head Nurse"),
            await fixture.Staff.SignInAsEmployeeAsync(garcia, "Head Nurse"));
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

    private static async Task<JsonElement> EncounterAsync(HttpClient client, string encounterId) =>
        await client.GetFromJsonAsync<JsonElement>($"{Encounters}/{encounterId}", Ct);

    private static uint StayVersion(JsonElement encounter) => encounter.GetProperty("admission").GetProperty("version").GetUInt32();

    private static async Task<HttpResponseMessage> TransferAsync(
        HttpClient client,
        string encounterId,
        string bedId,
        uint version,
        string reason = "Needs cardiac monitoring") =>
        await client.PostAsJsonAsync($"{Admissions}/{encounterId}/transfer", new { bedId, reason, version }, Ct);

    private static async Task<HttpResponseMessage> DischargeAsync(
        HttpClient client,
        string encounterId,
        uint version,
        string disposition = "home",
        string summary = "Pneumonia resolved on IV antibiotics. Home on oral amoxicillin for 5 days.") =>
        await client.PostAsJsonAsync($"{Admissions}/{encounterId}/discharge", new { disposition, summary, version }, Ct);

    /// <summary>The patient's id in each bed of the ward's census, by bed id (null when free).</summary>
    private static async Task<Dictionary<string, string?>> CensusAsync(HttpClient client, string wardId)
    {
        var wards = await client.GetFromJsonAsync<JsonElement>($"/api/clinical/census?wardId={wardId}", Ct);
        return wards.EnumerateArray()
            .SelectMany(w => w.GetProperty("beds").EnumerateArray())
            .ToDictionary(
                IdOf,
                b => b.GetProperty("patient").ValueKind == JsonValueKind.Null ? null : b.GetProperty("patient").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Ward_scope_follows_the_patient_when_a_head_nurse_transfers_them_to_another_ward()
    {
        var s = await AdmittedAsync();

        // Before: the Ward 3 nurse reaches the stay and the chart; the Ward 5 nurse reaches neither.
        var before = await EncounterAsync(s.Ward3Nurse, s.EncounterId);
        before.GetProperty("canTransfer").GetBoolean().ShouldBeTrue();
        before.GetProperty("canDischarge").GetBoolean().ShouldBeFalse();
        (await s.Ward5Nurse.GetAsync($"{Encounters}/{s.EncounterId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Ward5Nurse.GetAsync($"/api/patients/{s.PatientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The Ward 3 nurse sees the free beds on every ward, and moves the patient to Ward 5.
        var beds = await s.Ward3Nurse.GetFromJsonAsync<JsonElement>($"{Admissions}/{s.EncounterId}/free-beds", Ct);
        beds.EnumerateArray().SelectMany(w => w.GetProperty("beds").EnumerateArray()).Select(IdOf)
            .ShouldContain(s.Ward5Beds[0]);
        var moved = await TransferAsync(s.Ward3Nurse, s.EncounterId, s.Ward5Beds[0], StayVersion(before));
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        var result = await moved.ReadJsonAsync();
        result.GetProperty("ward").GetProperty("id").GetString().ShouldBe(s.Ward5);
        result.GetProperty("bed").GetProperty("id").GetString().ShouldBe(s.Ward5Beds[0]);
        result.GetProperty("canStillView").GetBoolean().ShouldBeFalse();

        // After, on the very next request: Ward 3 has lost the patient, Ward 5 has them.
        (await s.Ward3Nurse.GetAsync($"{Encounters}/{s.EncounterId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Ward3Nurse.GetAsync($"/api/patients/{s.PatientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Ward3Nurse.GetFromJsonAsync<JsonElement>($"{Encounters}?patientId={s.PatientId}", Ct))
            .GetProperty("total").GetInt32().ShouldBe(0);
        (await TransferAsync(s.Ward3Nurse, s.EncounterId, s.Ward3Beds[1], StayVersion(before)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var after = await EncounterAsync(s.Ward5Nurse, s.EncounterId);
        after.GetProperty("canTransfer").GetBoolean().ShouldBeTrue();
        after.GetProperty("canRecordVitals").GetBoolean().ShouldBeTrue();
        (await s.Ward5Nurse.GetAsync($"/api/patients/{s.PatientId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Ward5Nurse.GetFromJsonAsync<JsonElement>($"{Encounters}?patientId={s.PatientId}", Ct))
            .GetProperty("items").EnumerateArray().Select(IdOf).ShouldBe([s.EncounterId]);

        // The ward boards and the free-bed list follow too.
        (await CensusAsync(s.Fixture.Staff.Admin, s.Ward3))[s.Ward3Beds[0]].ShouldBeNull();
        (await CensusAsync(s.Fixture.Staff.Admin, s.Ward5))[s.Ward5Beds[0]].ShouldBe(s.PatientId);
    }

    [Fact]
    public async Task A_transfer_closes_the_current_bed_and_opens_the_next_with_the_reason()
    {
        var s = await AdmittedAsync();
        var doctor = await s.Fixture.Staff.SignInAsEmployeeAsync(s.PhysicianId, "Physician");
        var before = await EncounterAsync(doctor, s.EncounterId);

        var moved = await TransferAsync(doctor, s.EncounterId, s.Ward3Beds[1], StayVersion(before), "Closer to the nurses' station");

        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        (await moved.ReadJsonAsync()).GetProperty("canStillView").GetBoolean().ShouldBeTrue();
        var admission = (await EncounterAsync(doctor, s.EncounterId)).GetProperty("admission");
        admission.GetProperty("bed").GetProperty("id").GetString().ShouldBe(s.Ward3Beds[1]);
        admission.GetProperty("version").GetUInt32().ShouldNotBe(StayVersion(before));
        var history = admission.GetProperty("beds").EnumerateArray().ToList();
        history.Count.ShouldBe(2);
        history[0].GetProperty("bed").GetProperty("id").GetString().ShouldBe(s.Ward3Beds[0]);
        history[0].GetProperty("reason").GetString().ShouldBe("Admission");
        history[0].GetProperty("until").GetDateTimeOffset().ShouldBe(history[1].GetProperty("from").GetDateTimeOffset());
        history[1].GetProperty("reason").GetString().ShouldBe("Closer to the nurses' station");
        history[1].GetProperty("until").ValueKind.ShouldBe(JsonValueKind.Null);

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId)
            && e.Event == AuditEvents.Clinical.PatientTransferred.Key));
        var transferred = audit.ShouldHaveSingleItem();
        transferred.Severity.ShouldBe(AuditSeverity.Notice);
        transferred.Details!.ShouldContain("Bed 1");
        transferred.Details!.ShouldContain("Bed 2");
        transferred.Details!.ShouldContain("Closer to the nurses' station");
    }

    [Fact]
    public async Task The_attending_physician_discharges_and_the_summary_is_a_discharge_summary_note()
    {
        var s = await AdmittedAsync();
        var doctor = await s.Fixture.Staff.SignInAsEmployeeAsync(s.PhysicianId, "Physician");
        var before = await EncounterAsync(doctor, s.EncounterId);
        before.GetProperty("canDischarge").GetBoolean().ShouldBeTrue();

        var response = await DischargeAsync(doctor, s.EncounterId, StayVersion(before));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var stay = await response.ReadJsonAsync();
        stay.GetProperty("status").GetString().ShouldBe("closed");
        stay.GetProperty("canTransfer").GetBoolean().ShouldBeFalse();
        stay.GetProperty("canDischarge").GetBoolean().ShouldBeFalse();
        var admission = stay.GetProperty("admission");
        admission.GetProperty("dischargeDisposition").GetString().ShouldBe("home");
        admission.GetProperty("dischargedAt").GetDateTimeOffset().ShouldBe(stay.GetProperty("endedAt").GetDateTimeOffset());
        admission.GetProperty("beds")[0].GetProperty("until").GetDateTimeOffset().ShouldBe(admission.GetProperty("dischargedAt").GetDateTimeOffset());

        // The summary is an ordinary clinical note of the discharge_summary type, which its author can still edit.
        var notes = await doctor.GetFromJsonAsync<JsonElement>($"{Encounters}/{s.EncounterId}/notes", Ct);
        var note = notes.EnumerateArray().ShouldHaveSingleItem();
        note.GetProperty("noteType").GetString().ShouldBe("discharge_summary");
        note.GetProperty("body").GetString().ShouldStartWith("Pneumonia resolved");
        note.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        var edited = await doctor.PutAsJsonAsync(
            $"{Encounters}/{s.EncounterId}/notes/{IdOf(note)}",
            new { noteType = "discharge_summary", body = "Pneumonia resolved. Home on oral amoxicillin for 7 days.", version = note.GetProperty("version").GetUInt32() },
            Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync(Ct));

        // The bed is free, the board and the ward nurse no longer have the patient, and they can be admitted again.
        (await CensusAsync(s.Fixture.Staff.Admin, s.Ward3))[s.Ward3Beds[0]].ShouldBeNull();
        (await s.Ward3Nurse.GetAsync($"{Encounters}/{s.EncounterId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var again = await s.Fixture.FrontDesk.PostAsJsonAsync(
            Admissions,
            new { patientId = s.PatientId, attendingPhysicianId = s.PhysicianId, bedId = s.Ward3Beds[0], admittingDiagnosis = "Relapse", reason = "Fever again" },
            Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync(Ct));

        // Discharged, summary written and stay closed are one change; the summary text never reaches the log.
        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId)
            && (e.Event == AuditEvents.Clinical.PatientDischarged.Key
                || e.Event == AuditEvents.Clinical.NoteWritten.Key
                || e.Event == AuditEvents.Clinical.EncounterClosed.Key)));
        audit.Count.ShouldBe(3);
        audit.Select(e => e.CorrelationId).Distinct().Count().ShouldBe(1);
        audit.Single(e => e.Event == AuditEvents.Clinical.PatientDischarged.Key).Details!.ShouldContain("home");
        var all = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(s.PatientId)));
        all.ShouldAllBe(e => (e.Details == null || !e.Details.Contains("amoxicillin")) && (e.Changes == null || !e.Changes.Contains("amoxicillin")));
    }

    [Fact]
    public async Task Transfer_and_discharge_need_their_permissions()
    {
        var s = await AdmittedAsync();
        var before = await EncounterAsync(s.Fixture.Staff.Admin, s.EncounterId);
        var (staffNurse, _) = await factory.SignInWithRolesAsync("Staff Nurse");

        // The front desk admits but doesn't move or discharge; a staff nurse does neither; a head nurse moves but
        // doesn't discharge.
        (await TransferAsync(s.Fixture.FrontDesk, s.EncounterId, s.Ward3Beds[1], StayVersion(before)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TransferAsync(staffNurse, s.EncounterId, s.Ward3Beds[1], StayVersion(before)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Fixture.FrontDesk.GetAsync($"{Admissions}/{s.EncounterId}/free-beds", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DischargeAsync(s.Fixture.FrontDesk, s.EncounterId, StayVersion(before))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DischargeAsync(s.Ward3Nurse, s.EncounterId, StayVersion(before))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stay_outside_the_callers_scope_is_not_found_and_logged()
    {
        var s = await AdmittedAsync();
        var before = await EncounterAsync(s.Fixture.Staff.Admin, s.EncounterId);
        var (otherPhysician, _) = await s.Fixture.PhysicianWithSlotsAsync();
        var otherDoctor = await s.Fixture.Staff.SignInAsEmployeeAsync(otherPhysician, "Physician");

        var notAttending = await DischargeAsync(otherDoctor, s.EncounterId, StayVersion(before));
        var otherWard = await TransferAsync(s.Ward5Nurse, s.EncounterId, s.Ward5Beds[0], StayVersion(before));
        var otherWardBeds = await s.Ward5Nurse.GetAsync($"{Admissions}/{s.EncounterId}/free-beds", Ct);
        var unknown = await TransferAsync(s.Fixture.Staff.Admin, Guid.CreateVersion7().ToString(), s.Ward5Beds[0], 1);

        notAttending.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await notAttending.ProblemCodeAsync()).ShouldBe("admission_not_found");
        otherWard.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        otherWardBeds.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == Guid.Parse(s.EncounterId) && e.Event == AuditEvents.Access.Denied.Key)))
            .Count.ShouldBe(3);
        (await CensusAsync(s.Fixture.Staff.Admin, s.Ward3))[s.Ward3Beds[0]].ShouldBe(s.PatientId);
    }

    [Fact]
    public async Task Conflicts_and_business_rules_answer_with_problem_details()
    {
        var s = await AdmittedAsync();
        var doctor = await s.Fixture.Staff.SignInAsEmployeeAsync(s.PhysicianId, "Physician");
        var other = await s.Fixture.PatientAsync();
        var otherAdmit = await s.Fixture.FrontDesk.PostAsJsonAsync(
            Admissions,
            new { patientId = other, attendingPhysicianId = s.PhysicianId, bedId = s.Ward5Beds[0], admittingDiagnosis = "x", reason = "y" },
            Ct);
        otherAdmit.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stay = await EncounterAsync(doctor, s.EncounterId);
        var version = StayVersion(stay);

        var blank = await TransferAsync(doctor, s.EncounterId, s.Ward3Beds[1], version, reason: " ");
        var taken = await TransferAsync(doctor, s.EncounterId, s.Ward5Beds[0], version);
        var sameBed = await TransferAsync(doctor, s.EncounterId, s.Ward3Beds[0], version);
        var closeInstead = await doctor.PostAsJsonAsync($"{Encounters}/{s.EncounterId}/close", new { version = stay.GetProperty("version").GetUInt32() }, Ct);
        var noSummary = await DischargeAsync(doctor, s.EncounterId, version, summary: "");
        var badDisposition = await DischargeAsync(doctor, s.EncounterId, version, disposition: "recovered");

        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await blank.ReadJsonAsync()).GetProperty("errors").TryGetProperty("reason", out _).ShouldBeTrue();
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await taken.ProblemCodeAsync()).ShouldBe("bed_taken");
        sameBed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await sameBed.ProblemCodeAsync()).ShouldBe("same_bed");
        closeInstead.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await closeInstead.ProblemCodeAsync()).ShouldBe("discharge_to_close");
        noSummary.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noSummary.ReadJsonAsync()).GetProperty("errors").TryGetProperty("summary", out _).ShouldBeTrue();
        badDisposition.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Acting on a stale page: the stay moved meanwhile.
        (await TransferAsync(doctor, s.EncounterId, s.Ward3Beds[1], version)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await DischargeAsync(doctor, s.EncounterId, version);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency_conflict");

        // After discharge, nothing more moves.
        var fresh = StayVersion(await EncounterAsync(doctor, s.EncounterId));
        (await DischargeAsync(doctor, s.EncounterId, fresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var twice = await DischargeAsync(doctor, s.EncounterId, fresh);
        var moveAfter = await TransferAsync(doctor, s.EncounterId, s.Ward3Beds[0], fresh);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await twice.ProblemCodeAsync()).ShouldBe("patient_already_discharged");
        moveAfter.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await moveAfter.ProblemCodeAsync()).ShouldBe("patient_already_discharged");
    }

    [Fact]
    public async Task Discharging_needs_an_employee_record_to_sign_the_summary()
    {
        var s = await AdmittedAsync();
        var stay = await EncounterAsync(s.Fixture.Staff.Admin, s.EncounterId);
        stay.GetProperty("canDischarge").GetBoolean().ShouldBeFalse();

        var response = await DischargeAsync(s.Fixture.Staff.Admin, s.EncounterId, StayVersion(stay));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ProblemCodeAsync()).ShouldBe("no_employee_record");
    }

    [Fact]
    public async Task Two_transfers_racing_for_one_bed_leave_one_patient_in_it_and_the_other_where_they_were()
    {
        var s = await AdmittedAsync();
        var other = await s.Fixture.PatientAsync();
        var otherAdmit = await s.Fixture.FrontDesk.PostAsJsonAsync(
            Admissions,
            new { patientId = other, attendingPhysicianId = s.PhysicianId, bedId = s.Ward3Beds[1], admittingDiagnosis = "x", reason = "y" },
            Ct);
        var otherId = (await otherAdmit.ReadJsonAsync()).GetProperty("encounterId").GetString()!;
        var first = StayVersion(await EncounterAsync(s.Fixture.Staff.Admin, s.EncounterId));
        var second = StayVersion(await EncounterAsync(s.Fixture.Staff.Admin, otherId));
        var admin2 = (await factory.SignInWithRolesAsync("System Administrator")).Client;

        var responses = await Task.WhenAll(
            TransferAsync(s.Fixture.Staff.Admin, s.EncounterId, s.Ward5Beds[0], first),
            TransferAsync(admin2, otherId, s.Ward5Beds[0], second));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await loser.ProblemCodeAsync()).ShouldBe("bed_taken");
        var census = await CensusAsync(s.Fixture.Staff.Admin, s.Ward3);
        var ward5 = await CensusAsync(s.Fixture.Staff.Admin, s.Ward5);
        var (winnerPatient, loserBed, loserPatient) = responses[0] == loser
            ? (other, s.Ward3Beds[0], s.PatientId)
            : (s.PatientId, s.Ward3Beds[1], other);
        ward5[s.Ward5Beds[0]].ShouldBe(winnerPatient);
        census[loserBed].ShouldBe(loserPatient);
    }
}

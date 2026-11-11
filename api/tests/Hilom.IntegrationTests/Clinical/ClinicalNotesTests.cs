using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.IntegrationTests.Scheduling;
using Hilom.SharedKernel.Auditing;
using Npgsql;

namespace Hilom.IntegrationTests.Clinical;

[Collection(ApiTestGroup.Name)]
public sealed class ClinicalNotesTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(string encounterId) => $"/api/clinical/encounters/{encounterId}/notes";

    private static string IdOf(JsonElement element) => element.GetProperty("id").GetString()!;

    /// <summary>An open clinic visit and its attending physician, signed in.</summary>
    private async Task<(BookingFixture Fixture, string EncounterId, string PatientId, HttpClient Doctor)> VisitAsync()
    {
        var (fixture, physician, patient, appointment) = await EncounterFixture.DueNowAsync(factory);
        var encounterId = await EncounterFixture.CheckedInAsync(fixture, appointment);
        var doctor = await fixture.Staff.SignInAsEmployeeAsync(physician, "Physician");
        return (fixture, encounterId, patient, doctor);
    }

    private static async Task<JsonElement> WriteAsync(HttpClient client, string encounterId, string body = "Chest pain on exertion, likely stable angina", string type = "assessment")
    {
        var response = await client.PostAsJsonAsync(Url(encounterId), new { noteType = type, body }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    /// <summary>Moves a note's writing time back, past the 24-hour edit window.</summary>
    private async Task AgeAsync(string noteId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "update clinical.clinical_notes set authored_at = authored_at - interval '25 hours' where id = @id",
            connection);
        command.Parameters.AddWithValue("id", Guid.Parse(noteId));
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task The_attending_physician_writes_a_note_and_its_text_stays_out_of_the_audit_log()
    {
        var (_, encounterId, patient, doctor) = await VisitAsync();

        var note = await WriteAsync(doctor, encounterId);

        note.GetProperty("noteType").GetString().ShouldBe("assessment");
        note.GetProperty("authoredBy").GetString().ShouldStartWith("Jose Santos");
        note.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        note.GetProperty("canVoid").GetBoolean().ShouldBeTrue();
        note.GetProperty("voided").ValueKind.ShouldBe(JsonValueKind.Null);
        var list = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).EnumerateArray().ToList();
        list.ShouldHaveSingleItem().GetProperty("body").GetString()!.ShouldContain("stable angina");
        (await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct)).GetProperty("canWriteNotes").GetBoolean().ShouldBeTrue();

        var written = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.NoteWritten.Key)))
            .ShouldHaveSingleItem();
        written.Details!.ShouldContain("assessment");
        var all = await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient)));
        all.ShouldAllBe(e => (e.Changes == null || !e.Changes.Contains("angina")) && (e.Details == null || !e.Details.Contains("angina")));
    }

    [Fact]
    public async Task The_author_edits_within_24_hours_with_the_current_version()
    {
        var (_, encounterId, patient, doctor) = await VisitAsync();
        var note = await WriteAsync(doctor, encounterId);
        var url = $"{Url(encounterId)}/{IdOf(note)}";

        var edited = await doctor.PutAsJsonAsync(url, new { noteType = "progress", body = "Pain eased after rest", version = note.GetProperty("version").GetUInt32() }, Ct);
        var stale = await doctor.PutAsJsonAsync(url, new { noteType = "progress", body = "Again", version = note.GetProperty("version").GetUInt32() }, Ct);

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync(Ct));
        var body = await edited.ReadJsonAsync();
        body.GetProperty("noteType").GetString().ShouldBe("progress");
        body.GetProperty("editedAt").ValueKind.ShouldBe(JsonValueKind.String);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var changed = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.NoteChanged.Key)))
            .ShouldHaveSingleItem();
        changed.Details!.ShouldContain("body");
        changed.Details!.ShouldNotContain("Pain eased");
    }

    [Fact]
    public async Task After_24_hours_a_correction_replaces_the_note_and_voids_the_original()
    {
        var (_, encounterId, patient, doctor) = await VisitAsync();
        var note = await WriteAsync(doctor, encounterId);
        await AgeAsync(IdOf(note));
        var url = $"{Url(encounterId)}/{IdOf(note)}";
        var aged = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct))[0];
        aged.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        aged.GetProperty("canCorrect").GetBoolean().ShouldBeTrue();

        var edit = await doctor.PutAsJsonAsync(url, new { noteType = "assessment", body = "Late edit", version = aged.GetProperty("version").GetUInt32() }, Ct);
        var correction = await doctor.PostAsJsonAsync($"{url}/correct", new { noteType = "assessment", body = "Chest pain at rest; unstable angina", reason = "Wrong history taken" }, Ct);
        var again = await doctor.PostAsJsonAsync($"{url}/correct", new { noteType = "assessment", body = "Third try", reason = "Again" }, Ct);

        edit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await edit.ProblemCodeAsync()).ShouldBe("note_edit_window_closed");
        correction.StatusCode.ShouldBe(HttpStatusCode.Created, await correction.Content.ReadAsStringAsync(Ct));
        (await correction.ReadJsonAsync()).GetProperty("replacesNoteId").GetString().ShouldBe(IdOf(note));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("record_already_voided");

        var list = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct)).EnumerateArray().ToList();
        list.Count.ShouldBe(2);
        var original = list.Single(n => IdOf(n) == IdOf(note));
        original.GetProperty("voided").GetProperty("reason").GetString().ShouldBe("Wrong history taken");
        original.GetProperty("replacedByNoteId").GetString().ShouldBe(IdOf(list.Single(n => IdOf(n) != IdOf(note))));
        original.GetProperty("canVoid").GetBoolean().ShouldBeFalse();
        var voided = (await factory.AuditEntriesAsync(q => q.Where(e => e.PatientId == Guid.Parse(patient) && e.Event == AuditEvents.Clinical.RecordVoided.Key)))
            .ShouldHaveSingleItem();
        voided.Severity.ShouldBe(AuditSeverity.Critical);
        voided.Details!.ShouldContain("Wrong history taken");
    }

    [Fact]
    public async Task Notes_follow_the_encounter_scope_and_only_the_author_edits()
    {
        var (fixture, encounterId, _, doctor) = await VisitAsync();
        var (colleagueId, _) = await fixture.PhysicianWithSlotsAsync();
        var colleague = await fixture.Staff.SignInAsEmployeeAsync(colleagueId, "Physician");
        var chiefNurse = await fixture.Staff.SignInAsEmployeeAsync(await fixture.Staff.NurseAsync("Villanueva"), "Chief Nurse");
        var write = new { noteType = "nursing", body = "Settled, resting comfortably" };

        (await colleague.PostAsJsonAsync(Url(encounterId), write, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.FrontDesk.PostAsJsonAsync(Url(encounterId), write, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.FrontDesk.GetAsync(Url(encounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await colleague.GetAsync(Url(encounterId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var admin = await fixture.Staff.Admin.PostAsJsonAsync(Url(encounterId), write, Ct);
        admin.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.ProblemCodeAsync()).ShouldBe("no_employee_record");

        var nurseNote = await WriteAsync(chiefNurse, encounterId, "Settled, resting comfortably", "nursing");
        var url = $"{Url(encounterId)}/{IdOf(nurseNote)}";
        var asDoctor = (await doctor.GetFromJsonAsync<JsonElement>(Url(encounterId), Ct))[0];
        asDoctor.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        asDoctor.GetProperty("canVoid").GetBoolean().ShouldBeFalse();

        var edit = await doctor.PutAsJsonAsync(url, new { noteType = "nursing", body = "Changed", version = nurseNote.GetProperty("version").GetUInt32() }, Ct);
        var doctorVoid = await doctor.PostAsJsonAsync($"{url}/void", new { reason = "Not mine to keep" }, Ct);
        var doctorCorrect = await doctor.PostAsJsonAsync($"{url}/correct", new { noteType = "nursing", body = "Changed", reason = "x" }, Ct);
        var adminVoid = await fixture.Staff.Admin.PostAsJsonAsync($"{url}/void", new { reason = "Charted on the wrong patient" }, Ct);
        var adminAgain = await fixture.Staff.Admin.PostAsJsonAsync($"{url}/void", new { reason = "Again" }, Ct);

        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await edit.ProblemCodeAsync()).ShouldBe("not_note_author");
        doctorVoid.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        doctorCorrect.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        adminVoid.StatusCode.ShouldBe(HttpStatusCode.OK, await adminVoid.Content.ReadAsStringAsync(Ct));
        (await adminVoid.ReadJsonAsync()).GetProperty("voided").GetProperty("reason").GetString().ShouldBe("Charted on the wrong patient");
        adminAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.denied" && e.EntityId == Guid.Parse(IdOf(nurseNote))));
        denied.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Notes_need_a_kind_a_body_and_a_reason_to_void()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();
        var note = await WriteAsync(doctor, encounterId);

        var blank = await doctor.PostAsJsonAsync(Url(encounterId), new { noteType = "progress", body = "   " }, Ct);
        var discharge = await doctor.PostAsJsonAsync(Url(encounterId), new { noteType = "discharge_summary", body = "Home" }, Ct);
        var unknown = await doctor.PostAsJsonAsync(Url(encounterId), new { noteType = "poem", body = "Roses" }, Ct);
        var noReason = await doctor.PostAsJsonAsync($"{Url(encounterId)}/{IdOf(note)}/void", new { reason = " " }, Ct);
        var missing = await doctor.PostAsJsonAsync($"{Url(encounterId)}/{Guid.CreateVersion7()}/void", new { reason = "Wrong patient" }, Ct);

        (await blank.ReadJsonAsync()).GetProperty("errors").GetProperty("body")[0].GetString().ShouldBe("Write the note.");
        (await discharge.ReadJsonAsync()).GetProperty("errors").GetProperty("noteType")[0].GetString()!.ShouldContain("discharging");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noReason.ReadJsonAsync()).GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("Say why this entry is being voided.");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_closed_encounter_takes_no_new_notes_but_entries_can_still_be_voided()
    {
        var (_, encounterId, _, doctor) = await VisitAsync();
        var note = await WriteAsync(doctor, encounterId);
        var encounter = await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct);
        (await doctor.PostAsJsonAsync($"/api/clinical/encounters/{encounterId}/close", new { version = encounter.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var write = await doctor.PostAsJsonAsync(Url(encounterId), new { noteType = "progress", body = "Follow-up" }, Ct);
        var voided = await doctor.PostAsJsonAsync($"{Url(encounterId)}/{IdOf(note)}/void", new { reason = "Wrong patient" }, Ct);

        write.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await write.ProblemCodeAsync()).ShouldBe("encounter_not_open");
        voided.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await doctor.GetFromJsonAsync<JsonElement>($"/api/clinical/encounters/{encounterId}", Ct)).GetProperty("canWriteNotes").GetBoolean().ShouldBeFalse();
    }
}

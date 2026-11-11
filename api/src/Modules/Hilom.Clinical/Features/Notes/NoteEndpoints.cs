using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Features.Voiding;
using Hilom.Clinical.Scopes;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Features.Notes;

/// <summary>
/// Clinical notes on an encounter (docs/DATA_MODEL.md, clinical_notes): the list (<c>clinical.encounters.view</c>),
/// writing one on an open encounter and editing your own within 24 hours (<c>clinical.notes.write</c>), and after
/// that a correction that replaces it, voiding the original. Voiding on its own is <c>clinical.records.void</c>.
/// Note bodies never reach the audit log.
/// </summary>
internal static class NoteEndpoints
{
    private const string RecordType = "clinical_note";

    /// <summary>The encounter's notes, newest first, voided ones included. Logged as a view of the encounter.</summary>
    public static async Task<Results<Ok<List<ClinicalNoteResponse>>, ProblemHttpResult>> ListNotes(
        Guid encounterId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.EncountersView, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Clinical.EncounterViewed,
            new AuditOptions { Target = EncounterEndpoints.Target(encounter), PatientId = encounter.PatientId },
            cancellationToken);
        var access = await NoteAccess.ResolveAsync(db, permissions, staff, encounterId, cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, n => n.EncounterId == encounterId, access, time.GetUtcNow(), cancellationToken));
    }

    /// <summary>Writes a note on an open encounter, authored by the signed-in employee, now.</summary>
    public static async Task<Results<Created<ClinicalNoteResponse>, ProblemHttpResult>> WriteNote(
        Guid encounterId,
        WriteNoteRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.NotesWrite, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        if (encounter.Status != EncounterStatus.Open)
        {
            return ClinicalErrors.EncounterNotOpen.ToProblemResult();
        }

        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return ClinicalErrors.NoEmployeeRecord.ToProblemResult();
        }

        var note = new ClinicalNote(encounter.Id, encounter.PatientId, request.NoteType, request.Body, employeeId, time.GetUtcNow());
        db.Add(note);
        RecordWritten(auditLog, note);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/clinical/encounters/{encounterId}/notes", await ToResponseAsync(db, permissions, staff, note, time, cancellationToken));
    }

    /// <summary>The author edits their note within 24 hours of writing it. Records which fields changed, never the text.</summary>
    public static async Task<Results<Ok<ClinicalNoteResponse>, ProblemHttpResult>> EditNote(
        Guid encounterId,
        Guid id,
        EditNoteRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.NotesWrite, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        var note = await db.Set<ClinicalNote>().SingleOrDefaultAsync(n => n.Id == id && n.EncounterId == encounterId, cancellationToken);
        if (note is null)
        {
            return ClinicalErrors.NoteNotFound.ToProblemResult();
        }

        if (await staff.MyEmployeeIdAsync(cancellationToken) != note.AuthoredBy)
        {
            return ClinicalErrors.NotNoteAuthor.ToProblemResult();
        }

        if ((request.NoteType == NoteType.DischargeSummary) != (note.NoteType == NoteType.DischargeSummary))
        {
            return Error.Validation(
                    "invalid_note_type",
                    NoteRules.DischargeSummaryMessage,
                    new Dictionary<string, string[]> { ["noteType"] = [NoteRules.DischargeSummaryMessage] })
                .ToProblemResult();
        }

        var changed = note.Edit(request.NoteType, request.Body, time.GetUtcNow());
        if (!changed.IsSuccess)
        {
            return changed.Error.ToProblemResult();
        }

        if (changed.Value.Count == 0)
        {
            auditLog.NothingToRecord();
        }
        else
        {
            // Which fields changed, never the note's text (docs/AUDIT.md, rule 7).
            auditLog.Record(
                AuditEvents.Clinical.NoteChanged,
                new AuditOptions
                {
                    Target = Target(note),
                    PatientId = note.PatientId,
                    Details = new { encounterId, noteType = note.NoteType, fields = changed.Value },
                });
        }

        // Always write the row, so a stale version is caught even when nothing changed.
        var entry = db.Entry(note);
        entry.Property(n => n.Version).OriginalValue = request.Version;
        entry.Property(n => n.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ToResponseAsync(db, permissions, staff, note, time, cancellationToken));
    }

    /// <summary>
    /// A correction after the edit window (or by someone other than the author): a new note by the signed-in
    /// employee replaces this one, which is voided with the reason, in one transaction.
    /// </summary>
    public static async Task<Results<Created<ClinicalNoteResponse>, ProblemHttpResult>> CorrectNote(
        Guid encounterId,
        Guid id,
        CorrectNoteRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.NotesWrite, encounterId, track: false, cancellationToken);
        if (encounter is null)
        {
            return ClinicalErrors.EncounterNotFound.ToProblemResult();
        }

        if (await staff.MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return ClinicalErrors.NoEmployeeRecord.ToProblemResult();
        }

        var voided = await RecordVoiding.VoidAsync<ClinicalNote>(
            db, permissions, staff, currentUser, auditLog, time, encounterId, id, request.Reason, RecordType, cancellationToken);
        if (!voided.IsSuccess)
        {
            return voided.Error.ToProblemResult();
        }

        var replacement = new ClinicalNote(
            encounter.Id, encounter.PatientId, request.NoteType, request.Body, employeeId, time.GetUtcNow(), replacesNoteId: voided.Value.Id);
        db.Add(replacement);
        RecordWritten(auditLog, replacement);

        // A second correction of the same note fails on the void (already voided) or the unique index (409).
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/clinical/encounters/{encounterId}/notes", await ToResponseAsync(db, permissions, staff, replacement, time, cancellationToken));
    }

    /// <summary>Voids a note entered in error without replacing it. It stays in the list, struck through.</summary>
    public static async Task<Results<Ok<ClinicalNoteResponse>, ProblemHttpResult>> VoidNote(
        Guid encounterId,
        Guid id,
        VoidRecordRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var voided = await RecordVoiding.VoidAsync<ClinicalNote>(
            db, permissions, staff, currentUser, auditLog, time, encounterId, id, request.Reason, RecordType, cancellationToken);
        if (!voided.IsSuccess)
        {
            return voided.Error.ToProblemResult();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, permissions, staff, voided.Value, time, cancellationToken));
    }

    private static void RecordWritten(IAuditLog auditLog, ClinicalNote note) =>
        auditLog.Record(
            AuditEvents.Clinical.NoteWritten,
            new AuditOptions
            {
                Target = Target(note),
                PatientId = note.PatientId,
                Details = new { encounterId = note.EncounterId, noteType = note.NoteType, replacesNoteId = note.ReplacesNoteId },
            });

    private static AuditTarget Target(ClinicalNote note) => new(nameof(ClinicalNote), note.Id);

    private static async Task<ClinicalNoteResponse> ToResponseAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ClinicalNote note,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var access = await NoteAccess.ResolveAsync(db, permissions, staff, note.EncounterId, cancellationToken);
        var id = note.Id;
        return (await ToResponsesAsync(db, n => n.Id == id, access, time.GetUtcNow(), cancellationToken))[0];
    }

    private static async Task<List<ClinicalNoteResponse>> ToResponsesAsync(
        AppDbContext db,
        System.Linq.Expressions.Expression<Func<ClinicalNote, bool>> filter,
        NoteAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from n in db.Set<ClinicalNote>().AsNoTracking().Where(filter)
            join e in db.Set<Employee>() on n.AuthoredBy equals e.Id
            orderby n.AuthoredAt descending, n.Id descending
            select new { Note = n, Name = e.FirstName + " " + e.LastName })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Note.Id).ToList();
        var replacedBy = await db.Set<ClinicalNote>().AsNoTracking()
            .Where(n => n.ReplacesNoteId != null && ids.Contains(n.ReplacesNoteId.Value))
            .ToDictionaryAsync(n => n.ReplacesNoteId!.Value, n => n.Id, cancellationToken);
        var voidedBy = await RecordVoiding.VoidedByNamesAsync(db, rows.Select(r => r.Note), cancellationToken);

        return
        [
            .. rows.Select(r =>
            {
                var n = r.Note;
                return new ClinicalNoteResponse(
                    n.Id,
                    n.NoteType,
                    n.Body,
                    n.AuthoredAt,
                    r.Name,
                    n.EditedAt,
                    n.AuthoredAt + ClinicalNote.EditWindow,
                    n.ReplacesNoteId,
                    replacedBy.TryGetValue(n.Id, out var next) ? next : null,
                    RecordVoiding.ToResponse(n, voidedBy),
                    access.CanEdit(n, now),
                    access.CanCorrect(n),
                    access.Void.CanVoid(n),
                    n.Version);
            }),
        ];
    }

    /// <summary>What the caller may do with notes on one encounter.</summary>
    /// <param name="CanWrite"><c>clinical.notes.write</c> reaches the encounter and the caller has an employee record.</param>
    private sealed record NoteAccess(bool CanWrite, Guid? EmployeeId, VoidReach Void)
    {
        public static async Task<NoteAccess> ResolveAsync(
            AppDbContext db,
            ICurrentPermissions permissions,
            IStaffScope staff,
            Guid encounterId,
            CancellationToken cancellationToken)
        {
            var employeeId = await staff.MyEmployeeIdAsync(cancellationToken);
            var write = await EncounterReach.ResolveAsync(db, permissions, staff, Perm.Clinical.NotesWrite, cancellationToken);
            var canWrite = employeeId is not null
                && await write.Filter(db.Set<Encounter>().Where(e => e.Id == encounterId)).AnyAsync(cancellationToken);
            return new NoteAccess(canWrite, employeeId, await VoidReach.ResolveAsync(permissions, staff, cancellationToken));
        }

        public bool CanEdit(ClinicalNote note, DateTimeOffset now) => CanWrite && note.AuthoredBy == EmployeeId && note.IsEditable(now);

        public bool CanCorrect(ClinicalNote note) => CanWrite && Void.CanVoid(note);
    }
}

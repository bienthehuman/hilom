using System.Globalization;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.Patients.Domain;
using Hilom.Patients.Persistence;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Patients.Features.Registry;

/// <summary>The patient registry: search, open a record, register, edit demographics (docs/DATA_MODEL.md, patients).</summary>
internal static class PatientEndpoints
{
    private static readonly string[] SortFields = ["name", "mrn", "birthDate", "registeredAt"];

    public static async Task<Ok<PagedResponse<PatientSummaryResponse>>> ListPatients(
        AppDbContext db,
        ICurrentPermissions permissions,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        DateOnly? birthDate,
        CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var (field, descending) = Paging.ParseSort(sort, SortFields, "name");
        var scope = await permissions.ScopeForAsync(Perm.Patients.View, cancellationToken);

        var query = db.Set<Patient>().AsNoTracking().WhereInScope(scope, db.Set<PatientAccountLink>());

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern)
                || EF.Functions.ILike(p.LastName + ", " + p.FirstName, pattern)
                || (p.MiddleName != null && EF.Functions.ILike(p.FirstName + " " + p.MiddleName + " " + p.LastName, pattern))
                || EF.Functions.ILike(p.Mrn, pattern));
        }

        if (birthDate is { } born)
        {
            query = query.Where(p => p.BirthDate == born);
        }

        query = (field, descending) switch
        {
            ("mrn", false) => query.OrderBy(p => p.Mrn),
            ("mrn", true) => query.OrderByDescending(p => p.Mrn),
            ("birthDate", false) => query.OrderBy(p => p.BirthDate).ThenBy(p => p.Mrn),
            ("birthDate", true) => query.OrderByDescending(p => p.BirthDate).ThenByDescending(p => p.Mrn),
            ("registeredAt", false) => query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Mrn),
            ("registeredAt", true) => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Mrn),
            (_, true) => query.OrderByDescending(p => p.LastName).ThenByDescending(p => p.FirstName).ThenByDescending(p => p.Mrn),
            _ => query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Mrn),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(p => new PatientSummaryResponse(
                p.Id,
                p.Mrn,
                p.FirstName,
                p.MiddleName,
                p.LastName,
                p.BirthDate,
                p.Sex,
                p.ContactNumber,
                p.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<PatientSummaryResponse>(items, total, pageNumber, size));
    }

    /// <summary>Opening a record is a sensitive read: logged once per user per patient per 15 minutes.</summary>
    public static async Task<Results<Ok<PatientResponse>, ProblemHttpResult>> GetPatient(
        Guid id,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var patient = await FindInScopeAsync(db, permissions, auditLog, Perm.Patients.View, id, track: false, cancellationToken);
        if (patient is null)
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        await RecordViewAsync(db, permissions, auditLog, patient, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, patient, cancellationToken));
    }

    public static async Task<Created<PatientResponse>> RegisterPatient(
        RegisterPatientRequest request,
        AppDbContext db,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var patient = Patient.Register(await NextMrnAsync(db, cancellationToken), request.ToDetails());
        db.Add(patient);
        auditLog.Record(
            AuditEvents.Patients.PatientRegistered,
            new AuditOptions
            {
                Target = Target(patient),
                PatientId = patient.Id,
                Details = new { mrn = patient.Mrn },
            });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/patients/{patient.Id}", await ToResponseAsync(db, patient, cancellationToken));
    }

    public static async Task<Results<Ok<PatientResponse>, ProblemHttpResult>> UpdatePatient(
        Guid id,
        UpdatePatientRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var patient = await FindInScopeAsync(db, permissions, auditLog, Perm.Patients.Edit, id, track: true, cancellationToken);
        if (patient is null)
        {
            return PatientErrors.PatientNotFound.ToProblemResult();
        }

        var changed = patient.Update(request.ToDetails());
        if (changed.Count == 0)
        {
            auditLog.NothingToRecord();
        }
        else
        {
            // Field values are in the data-change entry of the same request; the event says which fields.
            auditLog.Record(
                AuditEvents.Patients.DemographicsChanged,
                new AuditOptions
                {
                    Target = Target(patient),
                    PatientId = patient.Id,
                    Details = new { mrn = patient.Mrn, fields = changed },
                });
        }

        // Always write the row, so a stale version is caught even when nothing changed.
        var entry = db.Entry(patient);
        entry.Property(p => p.Version).OriginalValue = request.Version;
        entry.Property(p => p.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await ToResponseAsync(db, patient, cancellationToken));
    }

    /// <summary>
    /// The patient, if it exists and <paramref name="permission"/>'s scope reaches it. A patient outside the scope
    /// is reported as not found, and the attempt is logged as <c>access.denied</c>.
    /// </summary>
    internal static async Task<Patient?> FindInScopeAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        Permission permission,
        Guid id,
        bool track,
        CancellationToken cancellationToken)
    {
        var patients = track ? db.Set<Patient>() : db.Set<Patient>().AsNoTracking();
        var patient = await patients.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (patient is null)
        {
            return null;
        }

        var scope = await permissions.ScopeForAsync(permission, cancellationToken);
        if (await db.Set<Patient>().Where(p => p.Id == id).WhereInScope(scope, db.Set<PatientAccountLink>()).AnyAsync(cancellationToken))
        {
            return patient;
        }

        await auditLog.RecordOutOfScopeAsync(permission, Target(patient), cancellationToken);
        return null;
    }

    /// <summary>
    /// Logs a record view (once per user per patient per 15 minutes). A portal user sees the patient through an
    /// account link, so the entry names that link too (docs/AUDIT.md).
    /// </summary>
    internal static async Task RecordViewAsync(
        AppDbContext db,
        ICurrentPermissions permissions,
        IAuditLog auditLog,
        Patient patient,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(Perm.Patients.View, cancellationToken);
        Guid? viaLink = null;
        if (!scope.IsAll && scope.UserId is { } userId)
        {
            viaLink = await db.Set<PatientAccountLink>()
                .ViewableBy(userId)
                .Where(l => l.PatientId == patient.Id)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        await auditLog.RecordReadAsync(
            AuditEvents.Patients.RecordViewed,
            new AuditOptions { Target = Target(patient), PatientId = patient.Id, ActingForPatientLinkId = viaLink },
            cancellationToken);
    }

    /// <summary><c>MRN-000123</c>: a never-reset counter, at least six digits.</summary>
    private static async Task<string> NextMrnAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var next = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{PatientsModule.SchemaName}.{PatientConfiguration.MrnSequence}') AS \"Value\"")
            .SingleAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"MRN-{next:D6}");
    }

    private static async Task<PatientResponse> ToResponseAsync(AppDbContext db, Patient p, CancellationToken cancellationToken)
    {
        var registeredBy = p.CreatedBy is { } userId
            ? await db.Set<User>().Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : null;

        return new PatientResponse(
            p.Id,
            p.Mrn,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            p.BirthDate,
            p.Sex,
            p.CivilStatus,
            p.Religion,
            p.Address,
            p.ContactNumber,
            p.EmergencyContactName,
            p.EmergencyContactRelation,
            p.EmergencyContactNumber,
            p.CreatedAt,
            registeredBy,
            p.Version);
    }

    internal static AuditTarget Target(Patient patient) => new(nameof(Patient), patient.Id);
}

using Hilom.Clinical.Domain;
using Hilom.Clinical.Features.Encounters;
using Hilom.Clinical.Features.Voiding;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Scopes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hilom.Clinical.Features.Vitals;

/// <summary>
/// Vital signs on an encounter (docs/DATA_MODEL.md, vital_signs): the list (<c>clinical.encounters.view</c>),
/// recording a set (<c>clinical.vitals.record</c>) and voiding one entered in error (<c>clinical.records.void</c>).
/// Every value is flagged against the shared ranges.
/// </summary>
internal static class VitalEndpoints
{
    /// <summary>Clock skew allowed between the recording device and the server.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static Ok<VitalRangesResponse> GetRanges(IOptions<VitalRangeOptions> options)
    {
        var r = options.Value;
        static VitalRangeResponse Map(VitalRange range) => new(range.NormalLow, range.NormalHigh, range.CriticalLow, range.CriticalHigh);
        return TypedResults.Ok(new VitalRangesResponse(
            Map(r.TemperatureC),
            Map(r.SystolicBp),
            Map(r.DiastolicBp),
            Map(r.HeartRate),
            Map(r.RespiratoryRate),
            Map(r.OxygenSaturation)));
    }

    /// <summary>The encounter's vital signs, newest first. Part of reading the encounter, so logged as a view of it.</summary>
    public static async Task<Results<Ok<List<VitalSignsResponse>>, ProblemHttpResult>> ListVitalSigns(
        Guid encounterId,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        IOptions<VitalRangeOptions> ranges,
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
        var rows = db.Set<VitalSigns>().Where(v => v.EncounterId == encounterId);
        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        return TypedResults.Ok(await ToResponsesAsync(db, rows, ranges.Value, reach, cancellationToken));
    }

    /// <summary>
    /// Records a set of vital signs on an open encounter, taken by the signed-in employee, now or earlier
    /// during the encounter.
    /// </summary>
    public static async Task<Results<Created<VitalSignsResponse>, ProblemHttpResult>> RecordVitalSigns(
        Guid encounterId,
        RecordVitalSignsRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        IAuditLog auditLog,
        IOptions<VitalRangeOptions> ranges,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var encounter = await EncounterEndpoints.FindInScopeAsync(
            db, permissions, staff, auditLog, Perm.Clinical.VitalsRecord, encounterId, track: false, cancellationToken);
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

        var now = time.GetUtcNow();
        var recordedAt = request.RecordedAt?.ToUniversalTime() ?? now;
        if (recordedAt > now + FutureTolerance)
        {
            return ClinicalErrors.InvalidRecordedAt("The time can't be in the future.").ToProblemResult();
        }

        if (recordedAt < encounter.StartedAt)
        {
            return ClinicalErrors.InvalidRecordedAt("The time can't be before the encounter started.").ToProblemResult();
        }

        var measurements = request.ToMeasurements();
        var vitals = new VitalSigns(encounter.Id, encounter.PatientId, employeeId, recordedAt, measurements, request.Notes);
        db.Add(vitals);
        var flags = Flags(vitals, ranges.Value);
        auditLog.Record(
            AuditEvents.Clinical.VitalsRecorded,
            new AuditOptions
            {
                Target = new AuditTarget(nameof(VitalSigns), vitals.Id),
                PatientId = encounter.PatientId,
                Details = new
                {
                    encounterId = encounter.Id,
                    measured = measurements.Taken(),
                    critical = Critical(flags),
                },
            });
        await db.SaveChangesAsync(cancellationToken);

        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        var response = (await ToResponsesAsync(db, db.Set<VitalSigns>().Where(v => v.Id == vitals.Id), ranges.Value, reach, cancellationToken))[0];
        return TypedResults.Created($"/api/clinical/encounters/{encounter.Id}/vital-signs", response);
    }

    /// <summary>Voids a set entered in error, e.g. on the wrong patient. It stays in the list, struck through.</summary>
    public static async Task<Results<Ok<VitalSignsResponse>, ProblemHttpResult>> VoidVitalSigns(
        Guid encounterId,
        Guid id,
        VoidRecordRequest request,
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        IOptions<VitalRangeOptions> ranges,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var voided = await RecordVoiding.VoidAsync<VitalSigns>(
            db, permissions, staff, currentUser, auditLog, time, encounterId, id, request.Reason, "vital_signs", cancellationToken);
        if (!voided.IsSuccess)
        {
            return voided.Error.ToProblemResult();
        }

        await db.SaveChangesAsync(cancellationToken);

        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync(db, db.Set<VitalSigns>().Where(v => v.Id == id), ranges.Value, reach, cancellationToken))[0]);
    }

    internal static VitalFlagsResponse Flags(VitalSigns v, VitalRangeOptions r) => new(
        v.TemperatureC is { } t ? r.TemperatureC.Flag(t) : null,
        v.SystolicBp is { } s ? r.SystolicBp.Flag(s) : null,
        v.DiastolicBp is { } d ? r.DiastolicBp.Flag(d) : null,
        v.HeartRate is { } h ? r.HeartRate.Flag(h) : null,
        v.RespiratoryRate is { } rr ? r.RespiratoryRate.Flag(rr) : null,
        v.OxygenSaturation is { } o ? r.OxygenSaturation.Flag(o) : null);

    /// <summary>Names of measures in a critical range, for the audit log.</summary>
    private static string[] Critical(VitalFlagsResponse f) =>
    [
        .. new (string Name, VitalFlag? Flag)[]
        {
            ("temperatureC", f.TemperatureC),
            ("systolicBp", f.SystolicBp),
            ("diastolicBp", f.DiastolicBp),
            ("heartRate", f.HeartRate),
            ("respiratoryRate", f.RespiratoryRate),
            ("oxygenSaturation", f.OxygenSaturation),
        }.Where(m => m.Flag is VitalFlag.CriticalLow or VitalFlag.CriticalHigh).Select(m => m.Name),
    ];

    private static async Task<List<VitalSignsResponse>> ToResponsesAsync(
        AppDbContext db,
        IQueryable<VitalSigns> vitals,
        VitalRangeOptions ranges,
        VoidReach reach,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from v in vitals.AsNoTracking()
            join e in db.Set<Employee>() on v.RecordedBy equals e.Id
            orderby v.RecordedAt descending, v.Id descending
            select new { Vitals = v, Name = e.FirstName + " " + e.LastName })
            .ToListAsync(cancellationToken);
        var voidedBy = await RecordVoiding.VoidedByNamesAsync(db, rows.Select(r => r.Vitals), cancellationToken);

        return
        [
            .. rows.Select(r =>
            {
                var v = r.Vitals;
                return new VitalSignsResponse(
                    v.Id,
                    v.RecordedAt,
                    r.Name,
                    v.TemperatureC,
                    v.SystolicBp,
                    v.DiastolicBp,
                    v.HeartRate,
                    v.RespiratoryRate,
                    v.OxygenSaturation,
                    v.PainScore,
                    v.WeightKg,
                    v.HeightCm,
                    v.Bmi,
                    v.Notes,
                    Flags(v, ranges),
                    RecordVoiding.ToResponse(v, voidedBy),
                    reach.CanVoid(v));
            }),
        ];
    }
}

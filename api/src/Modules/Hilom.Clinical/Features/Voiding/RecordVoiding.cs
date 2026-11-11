using FluentValidation;
using Hilom.Access.Domain;
using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Authorization;
using Hilom.SharedKernel.Results;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Scopes;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Clinical.Features.Voiding;

/// <param name="Reason">Why it was entered in error, e.g. "wrong patient". Shown with the struck-through entry.</param>
public sealed record VoidRecordRequest(string Reason);

/// <summary>How a voided entry was voided. Null on entries that stand.</summary>
/// <param name="By">The user who voided it.</param>
public sealed record VoidedResponse(DateTimeOffset At, string? By, string Reason);

internal sealed class VoidRecordRequestValidator : AbstractValidator<VoidRecordRequest>
{
    public VoidRecordRequestValidator() =>
        RuleFor(r => r.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Say why this entry is being voided.")
            .MaximumLength(ClinicalRecord.VoidReasonMaxLength);
}

/// <summary>
/// Who may void which entries (<c>clinical.records.void</c>, docs/RBAC.md): <c>all</c>, or <c>own</c> = entries
/// the signed-in employee entered. Resolved once per request, then asked per entry.
/// </summary>
internal sealed record VoidReach(bool All, Guid? EmployeeId)
{
    public static async Task<VoidReach> ResolveAsync(ICurrentPermissions permissions, IStaffScope staff, CancellationToken cancellationToken)
    {
        var scope = await permissions.ScopeForAsync(Perm.Clinical.RecordsVoid, cancellationToken);
        if (scope.IsAll)
        {
            return new VoidReach(true, null);
        }

        return new VoidReach(false, scope.Includes(PermissionScope.Own) ? await staff.MyEmployeeIdAsync(cancellationToken) : null);
    }

    public bool Reaches(ClinicalRecord record) => All || (EmployeeId is { } me && record.AuthorId == me);

    /// <summary>Whether the caller may void it now: in reach and not voided already.</summary>
    public bool CanVoid(ClinicalRecord record) => !record.IsVoided && Reaches(record);
}

/// <summary>
/// The one void flow for vital signs, notes and (3.8) medication administrations: find the entry on the
/// encounter, check the void scope, mark it with the reason and log <c>clinical.record.voided</c> (critical).
/// The entry stays in every list, struck through. The caller saves.
/// </summary>
internal static class RecordVoiding
{
    /// <summary>
    /// Voids <typeparamref name="T"/> <paramref name="id"/> on <paramref name="encounterId"/>. One outside the
    /// caller's void scope is reported as not found, and the attempt is logged as <c>access.denied</c>.
    /// </summary>
    /// <param name="recordType">Names the kind of entry in the audit log, e.g. <c>vital_signs</c>.</param>
    public static async Task<Result<T>> VoidAsync<T>(
        AppDbContext db,
        ICurrentPermissions permissions,
        IStaffScope staff,
        ICurrentUser currentUser,
        IAuditLog auditLog,
        TimeProvider time,
        Guid encounterId,
        Guid id,
        string reason,
        string recordType,
        CancellationToken cancellationToken)
        where T : ClinicalRecord
    {
        var record = await db.Set<T>().SingleOrDefaultAsync(r => r.Id == id && r.EncounterId == encounterId, cancellationToken);
        if (record is null)
        {
            return Result.Failure<T>(ClinicalErrors.RecordNotFound);
        }

        var reach = await VoidReach.ResolveAsync(permissions, staff, cancellationToken);
        if (!reach.Reaches(record))
        {
            await auditLog.RecordOutOfScopeAsync(Perm.Clinical.RecordsVoid, new AuditTarget(typeof(T).Name, record.Id), cancellationToken);
            return Result.Failure<T>(ClinicalErrors.RecordNotFound);
        }

        var voided = record.Void(currentUser.UserId, reason, time.GetUtcNow());
        if (!voided.IsSuccess)
        {
            return Result.Failure<T>(voided.Error);
        }

        // The reason is the point of the entry, so it's logged (docs/AUDIT.md); the voided content isn't.
        auditLog.Record(
            AuditEvents.Clinical.RecordVoided,
            new AuditOptions
            {
                Target = new AuditTarget(typeof(T).Name, record.Id),
                PatientId = record.PatientId,
                Details = new { recordType, encounterId, reason = record.VoidReason },
            });
        return Result.Success(record);
    }

    /// <summary>Names of the users who voided <paramref name="records"/>, by user id.</summary>
    public static async Task<Dictionary<Guid, string>> VoidedByNamesAsync(
        AppDbContext db,
        IEnumerable<ClinicalRecord> records,
        CancellationToken cancellationToken)
    {
        var ids = records.Where(r => r.VoidedBy is not null).Select(r => r.VoidedBy!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    public static VoidedResponse? ToResponse(ClinicalRecord record, IReadOnlyDictionary<Guid, string> names) =>
        record.VoidedAt is { } at
            ? new VoidedResponse(at, record.VoidedBy is { } by ? names.GetValueOrDefault(by) : null, record.VoidReason ?? "")
            : null;
}

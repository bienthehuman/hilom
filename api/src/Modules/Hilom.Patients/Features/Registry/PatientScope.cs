using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Patients.Features.Registry;

/// <summary>
/// Lets a module above Patients say which patients a user reaches at a scope, e.g. Scheduling: a physician's
/// <c>own</c> patients are those with appointments with them; Clinical (phase 3) adds encounters and ward beds.
/// Patients can't depend on those modules, so they register sources here (docs/ARCHITECTURE.md §3, rule 5).
/// </summary>
public interface IPatientReachSource
{
    PermissionScope Scope { get; }

    /// <summary>Ids of the patients <paramref name="userId"/> reaches. Must translate to SQL (it becomes a subquery).</summary>
    IQueryable<Guid> PatientIds(Guid userId);
}

/// <summary>
/// Which patients a scope reaches (docs/RBAC.md). <c>all</c> reaches everyone. <c>linked</c> reaches patients the
/// account has an active, verified link to with <c>can_view_records</c>. <c>own</c> and <c>ward</c> come from
/// <see cref="IPatientReachSource"/>s; with none registered for a scope, it reaches no one. Scopes combine as a union.
/// </summary>
internal sealed class PatientReach(AppDbContext db, IEnumerable<IPatientReachSource> sources)
{
    public IQueryable<Patient> Filter(IQueryable<Patient> patients, AccessScope scope)
    {
        if (scope.IsAll)
        {
            return patients;
        }

        if (scope.UserId is not { } userId)
        {
            return patients.Where(_ => false);
        }

        IQueryable<Guid>? reached = null;
        if (scope.Includes(PermissionScope.Linked))
        {
            reached = db.Set<PatientAccountLink>().ViewableBy(userId).Select(l => l.PatientId!.Value);
        }

        foreach (var source in sources.Where(s => scope.Includes(s.Scope)))
        {
            var ids = source.PatientIds(userId);
            reached = reached is null ? ids : reached.Union(ids);
        }

        return reached is null ? patients.Where(_ => false) : patients.Where(p => reached.Contains(p.Id));
    }
}

internal static class PatientLinkQueries
{
    /// <summary>Active links through which the account may see the patient's records.</summary>
    public static IQueryable<PatientAccountLink> ViewableBy(this IQueryable<PatientAccountLink> links, Guid userId) =>
        links.Where(l => l.UserId == userId && l.Status == LinkStatus.Active && l.CanViewRecords);
}

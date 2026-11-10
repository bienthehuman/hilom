using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Security;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Workforce.Scopes;

/// <summary>
/// Who the signed-in user is as staff: their employee record and the wards they're assigned to today
/// (hospital time). This is what the <c>own</c> and <c>ward</c> scopes resolve to (docs/RBAC.md). Loaded once
/// per request, and evaluated live: reassigning someone changes their access on the next request.
/// </summary>
public interface IStaffScope
{
    Task<Guid?> MyEmployeeIdAsync(CancellationToken cancellationToken);

    /// <summary>Wards the signed-in user is assigned to today. Usually one; empty when unassigned.</summary>
    Task<IReadOnlyList<Guid>> MyWardIdsTodayAsync(CancellationToken cancellationToken);
}

internal sealed class StaffScope(AppDbContext db, ICurrentUser currentUser, HospitalClock clock) : IStaffScope
{
    private (Guid? EmployeeId, IReadOnlyList<Guid> WardIds)? _loaded;

    public async Task<Guid?> MyEmployeeIdAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).EmployeeId;

    public async Task<IReadOnlyList<Guid>> MyWardIdsTodayAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).WardIds;

    private async Task<(Guid? EmployeeId, IReadOnlyList<Guid> WardIds)> LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded is { } loaded)
        {
            return loaded;
        }

        var employeeId = currentUser.UserId is { } userId
            ? await db.Set<Employee>().Where(e => e.UserId == userId).Select(e => (Guid?)e.Id).SingleOrDefaultAsync(cancellationToken)
            : null;

        var today = clock.Today;
        IReadOnlyList<Guid> wardIds = employeeId is { } id
            ? await db.Set<WardAssignment>()
                .Where(a => a.EmployeeId == id && a.Period.Contains(today))
                .Select(a => a.WardId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : [];

        _loaded = (employeeId, wardIds);
        return _loaded.Value;
    }
}

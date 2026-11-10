using Hilom.Facility.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Workforce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hilom.Workforce.Facility;

/// <summary>Answers Facility's questions about departments (they live here, in Workforce).</summary>
internal sealed class DepartmentDirectory(AppDbContext db) : IDepartmentDirectory
{
    public async Task<IReadOnlyDictionary<Guid, DepartmentInfo>> FindAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        ids.Count == 0
            ? new Dictionary<Guid, DepartmentInfo>()
            : await db.Set<Department>()
                .Where(d => ids.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => new DepartmentInfo(d.Id, d.Name, d.IsActive), cancellationToken);
}

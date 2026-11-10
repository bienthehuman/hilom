namespace Hilom.Facility.Domain;

public sealed record DepartmentInfo(Guid Id, string Name, bool IsActive);

/// <summary>
/// Departments live in Workforce, which sits above Facility (docs/ARCHITECTURE.md §3). Facility asks through
/// this interface and Workforce answers, so the dependency keeps pointing one way.
/// </summary>
public interface IDepartmentDirectory
{
    Task<IReadOnlyDictionary<Guid, DepartmentInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

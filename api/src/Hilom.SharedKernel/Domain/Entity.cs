namespace Hilom.SharedKernel.Domain;

/// <summary>
/// Base for every stored record: a time-ordered UUID v7 id, who/when audit fields,
/// and a concurrency token backed by PostgreSQL's <c>xmin</c> system column.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = EntityIds.Next();

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <summary>Row version. Sent to clients and checked on update to prevent silent overwrites.</summary>
    public uint Version { get; private set; }

    public void MarkCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
        UpdatedAt = at;
        UpdatedBy = by;
    }

    public void MarkUpdated(DateTimeOffset at, Guid? by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }
}

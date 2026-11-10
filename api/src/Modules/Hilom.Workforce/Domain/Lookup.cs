using Hilom.SharedKernel.Domain;

namespace Hilom.Workforce.Domain;

/// <summary>
/// A short reference list entry, such as a department. Names are unique (ignoring case). Entries are
/// deactivated instead of deleted so records that already use them stay valid (docs/DATA_MODEL.md).
/// </summary>
public abstract class Lookup : Entity
{
    public const int NameMaxLength = 100;

    protected Lookup()
    {
    }

    protected Lookup(string name) => Name = name.Trim();

    public string Name { get; private set; } = "";

    /// <summary>Inactive entries can't be picked for new records but still show on existing ones.</summary>
    public bool IsActive { get; private set; } = true;

    public void Rename(string name) => Name = name.Trim();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}

/// <summary>Lets generic code create a lookup and name it in messages ("Another department already has this name").</summary>
public interface ILookupKind<TSelf>
    where TSelf : Lookup, ILookupKind<TSelf>
{
    /// <summary>Lower-case singular noun, e.g. "department".</summary>
    static abstract string Noun { get; }

    static abstract TSelf Create(string name);
}

public sealed class Department : Lookup, ILookupKind<Department>
{
    private Department()
    {
    }

    private Department(string name)
        : base(name)
    {
    }

    public static string Noun => "department";

    public static Department Create(string name) => new(name);
}

public sealed class Position : Lookup, ILookupKind<Position>
{
    private Position()
    {
    }

    private Position(string name)
        : base(name)
    {
    }

    public static string Noun => "position";

    public static Position Create(string name) => new(name);
}

public sealed class Specialization : Lookup, ILookupKind<Specialization>
{
    private Specialization()
    {
    }

    private Specialization(string name)
        : base(name)
    {
    }

    public static string Noun => "specialization";

    public static Specialization Create(string name) => new(name);
}

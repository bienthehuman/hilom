using Hilom.SharedKernel.Domain;

namespace Hilom.Facility.Domain;

public enum WardType
{
    General,
    Pediatric,
    Icu,
    Maternity,
    Surgical,
    Isolation,
}

/// <summary>
/// A nursing unit, e.g. <c>W3</c> "Ward 3 – Medical". Capacity isn't stored: it's the number of active beds
/// (docs/DATA_MODEL.md). Wards are deactivated, never deleted.
/// </summary>
public sealed class Ward : Entity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;

    private Ward()
    {
    }

    public Ward(string code, string name, WardType wardType, Guid? departmentId)
    {
        Update(code, name, wardType, departmentId);
    }

    /// <summary>Short, unique (ignoring case) code staff use, e.g. <c>W3</c> or <c>ICU</c>.</summary>
    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public WardType WardType { get; private set; }

    /// <summary>The workforce department the ward belongs to, if any.</summary>
    public Guid? DepartmentId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public void Update(string code, string name, WardType wardType, Guid? departmentId)
    {
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        WardType = wardType;
        DepartmentId = departmentId;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}

/// <summary>A bed in a ward. Out-of-service beds are inactive. Occupancy comes from bed assignments (Clinical).</summary>
public sealed class Bed : Entity
{
    public const int LabelMaxLength = 20;

    private Bed()
    {
    }

    public Bed(Guid wardId, string label)
    {
        WardId = wardId;
        Label = label.Trim();
    }

    public Guid WardId { get; private set; }

    /// <summary>Unique within the ward (ignoring case), e.g. <c>3A</c> or <c>Bed 12</c>.</summary>
    public string Label { get; private set; } = "";

    public bool IsActive { get; private set; } = true;

    public void Relabel(string label) => Label = label.Trim();

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}

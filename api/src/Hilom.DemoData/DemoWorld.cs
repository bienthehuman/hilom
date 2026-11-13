using Hilom.SharedKernel.Domain;
using Hilom.Workforce.Domain;

namespace Hilom.DemoData;

internal sealed record Person(Guid UserId, Guid EmployeeId, string Name);

internal sealed record PhysicianInfo(Person Person, Guid SpecializationId, string Department, int ConsultMinutes);

internal sealed record BedInfo(Guid Id, string Label);

internal sealed record WardInfo(Guid Id, string Code, string Name, string Department, IReadOnlyList<BedInfo> Beds, Person HeadNurse, IReadOnlyList<Person> Nurses);

internal sealed class PatientInfo(Guid id, string mrn, string name, Sex sex, DateOnly birthDate, DateTimeOffset registeredAt)
{
    public Guid Id { get; } = id;

    public string Mrn { get; } = mrn;

    public string Name { get; } = name;

    public Sex Sex { get; } = sex;

    public DateOnly BirthDate { get; } = birthDate;

    public DateTimeOffset RegisteredAt { get; } = registeredAt;

    public List<string> Allergens { get; } = [];

    /// <summary>A family member's portal account linked to this patient, when there is one.</summary>
    public Guid? PortalUserId { get; set; }

    public bool PortalCanBook { get; set; }

    /// <summary>What this person is usually like, so their readings vary around something steady.</summary>
    public decimal HeightCm { get; init; }

    public decimal WeightKg { get; init; }

    public short Systolic { get; init; }
}

/// <summary>What the generators know about each other's work: who exists, where, and who is on duty.</summary>
internal sealed class DemoWorld
{
    public Dictionary<string, Guid> RoleIds { get; } = [];

    public Guid AdminUserId { get; set; }

    public Person Hr { get; set; } = null!;

    public Person ChiefNurse { get; set; } = null!;

    public List<Person> FrontDesk { get; } = [];

    public List<PhysicianInfo> Physicians { get; } = [];

    public List<WardInfo> Wards { get; } = [];

    public Dictionary<ShiftColor, Guid> ShiftIds { get; } = [];

    /// <summary>Who is rostered on each ward's shift on each day.</summary>
    public Dictionary<(Guid Ward, DateOnly Day, ShiftColor Shift), List<Person>> Roster { get; } = [];

    public List<PatientInfo> Patients { get; } = [];

    /// <summary>The scripted family: a patient admitted to Ward 3, and the daughter who follows his care in the portal.</summary>
    public PatientInfo? FeaturedPatient { get; set; }

    public string UnusablePasswordHash { get; set; } = "";

    public string DemoPasswordHash { get; set; } = "";
}

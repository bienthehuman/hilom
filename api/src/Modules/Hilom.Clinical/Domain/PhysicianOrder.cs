using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

public enum OrderType
{
    Medication,
    Laboratory,
    Diet,
    Nursing,
    Other,
}

public enum OrderStatus
{
    Active,

    /// <summary>Carried out in full. Nothing sets it yet; it arrives with the medication administration record.</summary>
    Completed,
    Discontinued,
}

/// <summary>The four fields a medication order needs. Each is required, so a dose is never left to be guessed.</summary>
public sealed record MedicationDetails(string Name, string Dose, string Route, string Frequency)
{
    public const int FieldMaxLength = 200;

    /// <summary>The fields that are blank, by JSON name.</summary>
    public IReadOnlyList<string> Missing() =>
    [
        .. new (string Field, string Value)[]
        {
            ("medicationName", Name),
            ("dose", Dose),
            ("route", Route),
            ("frequency", Frequency),
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Field),
    ];
}

/// <summary>
/// A physician's order on an encounter (docs/DATA_MODEL.md, physician_orders). Written by a physician and never
/// deleted: stopping one is a discontinuation with a reason, and it stays in the list. A medication order always
/// carries name, dose, route and frequency (a check constraint in the database says so too).
/// </summary>
public sealed class PhysicianOrder : Entity, IPatientRecord
{
    public const int DetailsMaxLength = 2_000;
    public const int ReasonMaxLength = 500;

    private PhysicianOrder()
    {
    }

    public PhysicianOrder(
        Guid encounterId,
        Guid patientId,
        Guid orderedBy,
        DateTimeOffset orderedAt,
        OrderType type,
        string details,
        MedicationDetails? medication)
    {
        EncounterId = encounterId;
        PatientId = patientId;
        OrderedBy = orderedBy;
        OrderedAt = orderedAt;
        OrderType = type;
        Details = details.Trim();
        if (type == OrderType.Medication && medication is { } m)
        {
            MedicationName = m.Name.Trim();
            Dose = m.Dose.Trim();
            Route = m.Route.Trim();
            Frequency = m.Frequency.Trim();
        }

        Status = OrderStatus.Active;
    }

    public Guid EncounterId { get; private set; }

    /// <summary>Copied from the encounter, so audit entries and the patient's history find it directly.</summary>
    public Guid PatientId { get; private set; }

    /// <summary>The physician (employee id) who ordered it.</summary>
    public Guid OrderedBy { get; private set; }

    public DateTimeOffset OrderedAt { get; private set; }

    public OrderType OrderType { get; private set; }

    /// <summary>Clinical text: never in the audit log.</summary>
    [SensitiveData]
    public string Details { get; private set; } = "";

    public string? MedicationName { get; private set; }

    public string? Dose { get; private set; }

    public string? Route { get; private set; }

    public string? Frequency { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset? DiscontinuedAt { get; private set; }

    /// <summary>The user who discontinued it. A user, not an employee, so an administrator without an employee record can.</summary>
    public Guid? DiscontinuedBy { get; private set; }

    public string? DiscontinueReason { get; private set; }

    public bool IsActive => Status == OrderStatus.Active;

    /// <summary>Stops an active order. The reason is required and stays with the order.</summary>
    public Result Discontinue(Guid? by, string reason, DateTimeOffset at)
    {
        if (!IsActive)
        {
            return Result.Failure(ClinicalErrors.OrderNotActive);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(ClinicalErrors.DiscontinueReasonRequired);
        }

        Status = OrderStatus.Discontinued;
        DiscontinuedAt = at;
        DiscontinuedBy = by;
        DiscontinueReason = reason.Trim();
        return Result.Success();
    }
}

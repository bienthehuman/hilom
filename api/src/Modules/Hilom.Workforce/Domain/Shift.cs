using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Workforce.Domain;

/// <summary>Which shift color token the shift uses (<c>shift-am</c>, <c>shift-pm</c>, <c>shift-noc</c>).</summary>
public enum ShiftColor
{
    Am,
    Pm,
    Noc,
}

/// <summary>
/// A shift definition, e.g. <c>AM</c> 06:00–14:00. A shift whose end is earlier than its start crosses
/// midnight and ends the next day (docs/DATA_MODEL.md). Deactivated, never deleted: roster entries use it.
/// </summary>
public sealed class Shift : Entity
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 50;

    private Shift()
    {
    }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public TimeOnly StartsAt { get; private set; }

    public TimeOnly EndsAt { get; private set; }

    public ShiftColor Color { get; private set; }

    /// <summary>Order in pickers and the roster grid (e.g. AM, PM, NOC).</summary>
    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Ends the next day, e.g. 22:00–06:00.</summary>
    public bool CrossesMidnight => EndsAt < StartsAt;

    /// <summary>Length of the shift. <see cref="TimeOnly"/> subtraction already wraps past midnight (06:00 − 22:00 = 8 h).</summary>
    public TimeSpan Duration => EndsAt - StartsAt;

    public static Result<Shift> Create(string code, string name, TimeOnly startsAt, TimeOnly endsAt, ShiftColor color, int sortOrder)
    {
        var shift = new Shift();
        var updated = shift.Update(code, name, startsAt, endsAt, color, sortOrder);
        return updated.IsSuccess ? Result.Success(shift) : Result.Failure<Shift>(updated.Error);
    }

    public Result Update(string code, string name, TimeOnly startsAt, TimeOnly endsAt, ShiftColor color, int sortOrder)
    {
        if (startsAt == endsAt)
        {
            return Result.Failure(WorkforceErrors.InvalidShift("endsAt", "A shift can't start and end at the same time."));
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Color = color;
        SortOrder = sortOrder;
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}

using FluentValidation;
using Hilom.Workforce.Domain;

namespace Hilom.Workforce.Features.Shifts;

/// <param name="CrossesMidnight">Ends the next day (end time earlier than start time).</param>
/// <param name="DurationMinutes">Length of the shift, including across midnight.</param>
/// <param name="Version">Send back when saving; a mismatch means someone else changed the shift meanwhile.</param>
public sealed record ShiftResponse(
    Guid Id,
    string Code,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    bool CrossesMidnight,
    int DurationMinutes,
    ShiftColor Color,
    int SortOrder,
    bool IsActive,
    uint Version)
{
    internal static ShiftResponse From(Shift s) =>
        new(s.Id, s.Code, s.Name, s.StartsAt, s.EndsAt, s.CrossesMidnight, (int)s.Duration.TotalMinutes, s.Color, s.SortOrder, s.IsActive, s.Version);
}

public enum ShiftStatusFilter
{
    Active,
    Inactive,
}

public interface IShiftFields
{
    string Code { get; }

    string Name { get; }

    TimeOnly? StartsAt { get; }

    TimeOnly? EndsAt { get; }

    ShiftColor? Color { get; }

    int? SortOrder { get; }
}

public sealed record CreateShiftRequest(string Code, string Name, TimeOnly? StartsAt, TimeOnly? EndsAt, ShiftColor? Color, int? SortOrder)
    : IShiftFields;

public sealed record UpdateShiftRequest(
    string Code,
    string Name,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    ShiftColor? Color,
    int? SortOrder,
    uint Version) : IShiftFields;

internal static class ShiftRules
{
    public static void AddShiftRules<T>(this AbstractValidator<T> validator)
        where T : IShiftFields
    {
        validator.RuleFor(r => r.Code)
            .NotEmpty().WithMessage("Enter a short code, like AM.")
            .MaximumLength(Shift.CodeMaxLength)
            .Matches("^[A-Za-z0-9]+$").WithMessage("Use letters and numbers only.");
        validator.RuleFor(r => r.Name).NotEmpty().WithMessage("Enter a name, like Morning.").MaximumLength(Shift.NameMaxLength);
        validator.RuleFor(r => r.StartsAt).NotNull().WithMessage("Enter the start time.");
        validator.RuleFor(r => r.EndsAt).NotNull().WithMessage("Enter the end time.");
        validator.RuleFor(r => r.Color).NotNull().WithMessage("Choose a color.");
        validator.RuleFor(r => r.SortOrder).InclusiveBetween(0, 999).When(r => r.SortOrder is not null);
    }
}

internal sealed class CreateShiftRequestValidator : AbstractValidator<CreateShiftRequest>
{
    public CreateShiftRequestValidator() => this.AddShiftRules();
}

internal sealed class UpdateShiftRequestValidator : AbstractValidator<UpdateShiftRequest>
{
    public UpdateShiftRequestValidator() => this.AddShiftRules();
}

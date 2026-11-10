using FluentValidation;
using Hilom.Infrastructure.Configuration;
using Hilom.Scheduling.Domain;

namespace Hilom.Scheduling.Features.Slots;

/// <summary>A physician whose availability the caller may manage.</summary>
/// <param name="Id">The physician's employee id.</param>
/// <param name="ConsultationMinutes">Their usual slot length, from the physician profile.</param>
public sealed record SlotPhysicianResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Specialization,
    int ConsultationMinutes,
    bool AcceptsAppointments);

public sealed record SlotResponse(Guid Id, Guid PhysicianId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, SlotStatus Status);

/// <param name="Skipped">Slots in the pattern that overlapped open slots the physician already had.</param>
public sealed record PublishSlotsResponse(int Created, int Skipped);

/// <summary>A weekly pattern, in hospital time. See <see cref="WeeklyPattern"/>.</summary>
/// <param name="SlotMinutes">Defaults to the physician's consultation length.</param>
public sealed record PublishSlotsRequest(
    Guid PhysicianId,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<DayOfWeek>? Days,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    int? SlotMinutes);

internal sealed class PublishSlotsRequestValidator : AbstractValidator<PublishSlotsRequest>
{
    public PublishSlotsRequestValidator(HospitalClock clock)
    {
        RuleFor(r => r.PhysicianId).NotEmpty().WithMessage("Choose a physician.");
        RuleFor(r => r.From)
            .NotNull().WithMessage("Enter the first day.")
            .Must(from => from >= clock.Today).WithMessage("Start today or later.");
        RuleFor(r => r.To)
            .NotNull().WithMessage("Enter the last day.")
            .GreaterThanOrEqualTo(r => r.From).When(r => r.From is not null).WithMessage("The last day can't be before the first.")
            .Must((r, to) => to!.Value.DayNumber - r.From!.Value.DayNumber < WeeklyPattern.MaxDays)
            .When(r => r.From is not null && r.To is not null && r.To >= r.From)
            .WithMessage($"Publish at most {WeeklyPattern.MaxDays / 7} weeks at a time.");
        RuleFor(r => r.Days).NotEmpty().WithMessage("Choose at least one day of the week.");
        RuleFor(r => r.StartTime).NotNull().WithMessage("Enter the start time.");
        RuleFor(r => r.EndTime)
            .NotNull().WithMessage("Enter the end time.")
            .GreaterThan(r => r.StartTime).When(r => r.StartTime is not null).WithMessage("The end time must be after the start time.");
        RuleFor(r => r.SlotMinutes)
            .InclusiveBetween(WeeklyPattern.MinSlotMinutes, WeeklyPattern.MaxSlotMinutes)
            .WithMessage($"Use {WeeklyPattern.MinSlotMinutes} to {WeeklyPattern.MaxSlotMinutes} minutes.")
            .When(r => r.SlotMinutes is not null);
    }
}

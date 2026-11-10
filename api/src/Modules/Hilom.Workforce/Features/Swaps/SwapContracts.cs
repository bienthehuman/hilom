using FluentValidation;
using Hilom.Workforce.Domain;
using Hilom.Workforce.Features.Assignments;
using Hilom.Workforce.Features.Roster;

namespace Hilom.Workforce.Features.Swaps;

/// <summary>Which side of a swap the signed-in person is on.</summary>
public enum SwapRole
{
    Requester,
    Target,
}

/// <summary>One side of a swap, as it was when the swap was asked for.</summary>
public sealed record SwapSide(AssignedEmployee Employee, DateOnly DutyDate, RosterShift Shift);

/// <param name="YouAre">Your side of the swap, when you're on one.</param>
/// <param name="Version">Send back with every action.</param>
public sealed record SwapResponse(
    Guid Id,
    SwapStatus Status,
    AssignedWard Ward,
    SwapSide Requester,
    SwapSide Target,
    string? Message,
    string? TargetResponse,
    DateTimeOffset? TargetRespondedAt,
    string? ReviewNote,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset CreatedAt,
    SwapRole? YouAre,
    uint Version);

/// <summary>A colleague's shift that could be swapped for yours.</summary>
public sealed record SwapOption(Guid EntryId, AssignedEmployee Employee, DateOnly DutyDate, RosterShift Shift);

/// <param name="RequesterEntryId">Your shift to give away.</param>
/// <param name="TargetEntryId">The colleague's shift you'd take instead.</param>
public sealed record RequestSwapRequest(Guid RequesterEntryId, Guid TargetEntryId, string? Message);

/// <param name="Note">Optional note, e.g. why a swap was declined or rejected.</param>
public sealed record SwapActionRequest(string? Note, uint Version);

internal sealed class RequestSwapRequestValidator : AbstractValidator<RequestSwapRequest>
{
    public RequestSwapRequestValidator()
    {
        RuleFor(r => r.RequesterEntryId).NotEmpty().WithMessage("Choose your shift.");
        RuleFor(r => r.TargetEntryId).NotEmpty().WithMessage("Choose the shift you'd take.");
        RuleFor(r => r.Message).MaximumLength(ShiftSwapRequest.NoteMaxLength);
    }
}

internal sealed class SwapActionRequestValidator : AbstractValidator<SwapActionRequest>
{
    public SwapActionRequestValidator() => RuleFor(r => r.Note).MaximumLength(ShiftSwapRequest.NoteMaxLength);
}

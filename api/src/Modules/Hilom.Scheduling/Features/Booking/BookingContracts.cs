using FluentValidation;
using Hilom.Scheduling.Domain;

namespace Hilom.Scheduling.Features.Booking;

/// <summary>A physician patients can book, for the booking filters.</summary>
public sealed record BookingPhysicianResponse(Guid Id, string FirstName, string LastName, Guid SpecializationId, string Specialization);

/// <summary>A free slot: open, in the future, and not held by an appointment. No patient data.</summary>
public sealed record OpenSlotResponse(Guid Id, BookingPhysicianResponse Physician, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record AppointmentPatientRef(Guid Id, string Mrn, string FirstName, string? MiddleName, string LastName);

/// <param name="BookedBy">Display name of whoever booked: staff, the patient, or a family member.</param>
/// <param name="DecisionNote">Why it was declined, or a note with the confirmation.</param>
/// <param name="CanCancel">Whether the caller may cancel it now (it's upcoming and they reach it to cancel).</param>
/// <param name="EncounterId">The visit opened by checking in, on staff lists. Opening it needs <c>clinical.encounters.view</c>.</param>
/// <param name="Version">Send back when acting on it; a mismatch means someone else acted meanwhile.</param>
public sealed record AppointmentResponse(
    Guid Id,
    AppointmentStatus Status,
    string Reason,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    BookingPhysicianResponse Physician,
    AppointmentPatientRef Patient,
    DateTimeOffset BookedAt,
    string? BookedBy,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    string? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    bool CanCancel,
    Guid? EncounterId,
    uint Version);

/// <param name="Reason">Why the patient wants to be seen, in their words.</param>
public sealed record BookAppointmentRequest(Guid SlotId, Guid PatientId, string Reason);

internal sealed class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentRequestValidator()
    {
        RuleFor(r => r.SlotId).NotEmpty().WithMessage("Choose a time.");
        RuleFor(r => r.PatientId).NotEmpty().WithMessage("Choose the patient.");
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("Say briefly why the patient wants to be seen.")
            .MaximumLength(Appointment.ReasonMaxLength);
    }
}

/// <param name="Note">Optional, shown to the patient's side.</param>
public sealed record ConfirmAppointmentRequest(string? Note, uint Version);

/// <param name="Reason">Shown to the patient's side, so they know what to do next.</param>
public sealed record DeclineAppointmentRequest(string Reason, uint Version);

public sealed record CancelAppointmentRequest(string Reason, uint Version);

public sealed record NoShowRequest(uint Version);

internal sealed class ConfirmAppointmentRequestValidator : AbstractValidator<ConfirmAppointmentRequest>
{
    public ConfirmAppointmentRequestValidator() => RuleFor(r => r.Note).MaximumLength(Appointment.NoteMaxLength);
}

internal sealed class DeclineAppointmentRequestValidator : AbstractValidator<DeclineAppointmentRequest>
{
    public DeclineAppointmentRequestValidator() =>
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("Say why, so the patient knows what to do next.")
            .MaximumLength(Appointment.NoteMaxLength);
}

internal sealed class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
{
    public CancelAppointmentRequestValidator() =>
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("Say why the appointment is cancelled.")
            .MaximumLength(Appointment.NoteMaxLength);
}

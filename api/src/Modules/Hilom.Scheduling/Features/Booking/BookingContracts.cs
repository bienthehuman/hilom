using FluentValidation;
using Hilom.Scheduling.Domain;

namespace Hilom.Scheduling.Features.Booking;

/// <summary>A physician patients can book, for the booking filters.</summary>
public sealed record BookingPhysicianResponse(Guid Id, string FirstName, string LastName, Guid SpecializationId, string Specialization);

/// <summary>A free slot: open, in the future, and not held by an appointment. No patient data.</summary>
public sealed record OpenSlotResponse(Guid Id, BookingPhysicianResponse Physician, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record AppointmentPatientRef(Guid Id, string Mrn, string FirstName, string? MiddleName, string LastName);

public sealed record AppointmentResponse(
    Guid Id,
    AppointmentStatus Status,
    string Reason,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    BookingPhysicianResponse Physician,
    AppointmentPatientRef Patient,
    DateTimeOffset BookedAt);

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

using FluentValidation;
using Hilom.Clinical.Domain;

namespace Hilom.Clinical.Features.Orders;

/// <param name="OrderedBy">The physician's name.</param>
/// <param name="MedicationName">Set on medication orders, with <c>Dose</c>, <c>Route</c> and <c>Frequency</c>; null on every other kind.</param>
/// <param name="Discontinued">Set when it was discontinued; it stays in the list.</param>
/// <param name="CanDiscontinue">The order is active and the caller may discontinue orders on this encounter (<c>clinical.orders.discontinue</c>: all, or own = they're the attending physician).</param>
/// <param name="Version">Send back when discontinuing; a mismatch means it changed meanwhile.</param>
public sealed record PhysicianOrderResponse(
    Guid Id,
    OrderType OrderType,
    OrderStatus Status,
    string Details,
    string? MedicationName,
    string? Dose,
    string? Route,
    string? Frequency,
    DateTimeOffset OrderedAt,
    string OrderedBy,
    DiscontinuedResponse? Discontinued,
    bool CanDiscontinue,
    uint Version);

/// <param name="By">The user who discontinued it.</param>
public sealed record DiscontinuedResponse(DateTimeOffset At, string? By, string Reason);

/// <param name="MedicationName">Required, with dose, route and frequency, when <c>OrderType</c> is <c>medication</c>. Ignored otherwise.</param>
public sealed record CreateOrderRequest(
    OrderType OrderType,
    string Details,
    string? MedicationName,
    string? Dose,
    string? Route,
    string? Frequency);

public sealed record DiscontinueOrderRequest(string Reason, uint Version);

internal sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.OrderType).IsInEnum().WithMessage("Choose the kind of order.");
        RuleFor(r => r.Details)
            .Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage("Write the order.")
            .MaximumLength(PhysicianOrder.DetailsMaxLength);

        // Medication orders: every field, so a dose is never left to be guessed. Also a check constraint.
        When(r => r.OrderType == OrderType.Medication, () =>
        {
            Field(r => r.MedicationName, "Name the medication.", "medicationName");
            Field(r => r.Dose, "Give the dose, e.g. 500 mg.", "dose");
            Field(r => r.Route, "Give the route, e.g. oral or IV.", "route");
            Field(r => r.Frequency, "Give the frequency, e.g. every 8 hours.", "frequency");
        });
    }

    private void Field(System.Linq.Expressions.Expression<Func<CreateOrderRequest, string?>> field, string required, string name) =>
        RuleFor(field)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage(required)
            .MaximumLength(MedicationDetails.FieldMaxLength)
            .OverridePropertyName(name);
}

internal sealed class DiscontinueOrderRequestValidator : AbstractValidator<DiscontinueOrderRequest>
{
    public DiscontinueOrderRequestValidator() =>
        RuleFor(r => r.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Say why the order is being discontinued.")
            .MaximumLength(PhysicianOrder.ReasonMaxLength);
}

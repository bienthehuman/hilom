using Hilom.SharedKernel.Results;

namespace Hilom.Scheduling.Domain;

/// <summary>Expected failures in the Scheduling module, with stable codes the web app relies on.</summary>
public static class SchedulingErrors
{
    public static readonly Error PhysicianNotFound = Error.NotFound("physician_not_found", "This physician doesn't exist.");

    public static readonly Error SlotNotFound = Error.NotFound("slot_not_found", "This slot doesn't exist.");

    public static readonly Error SlotNotOpen = Error.Conflict("slot_not_open", "This slot is already closed.");

    public static readonly Error PhysicianNotBookable = Error.BusinessRule(
        "physician_not_bookable",
        "This physician doesn't accept appointments, or no longer works here. Change that on their employee profile first.");

    public static readonly Error SlotsRaced = Error.Conflict(
        "slots_overlap",
        "Someone just published slots for the same physician at overlapping times. Reload and try again.");

    public static readonly Error SlotTaken = Error.Conflict(
        "slot_taken",
        "Someone else just booked this time. Choose another slot.");

    public static readonly Error SlotNotBookable = Error.Conflict(
        "slot_not_bookable",
        "This slot is no longer available: it was closed, or its time has passed.");

    public static Error InvalidPattern(string field, string message) =>
        Error.Validation("invalid_pattern", message, new Dictionary<string, string[]> { [field] = [message] });
}

using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

/// <summary>Expected failures in the Clinical module, with stable codes the web app relies on.</summary>
public static class ClinicalErrors
{
    public static readonly Error EncounterNotFound = Error.NotFound("encounter_not_found", "This encounter doesn't exist.");

    public static readonly Error EncounterNotOpen = Error.Conflict(
        "encounter_not_open",
        "This encounter is already closed.");

    public static readonly Error NoEmployeeRecord = Error.BusinessRule(
        "no_employee_record",
        "Only staff with an employee record can record this. Ask HR to link your account to your employee record.");

    public static Error InvalidRecordedAt(string message) =>
        Error.Validation("invalid_recorded_at", message, new Dictionary<string, string[]> { ["recordedAt"] = [message] });
}

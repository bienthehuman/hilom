using Hilom.SharedKernel.Results;

namespace Hilom.Clinical.Domain;

/// <summary>Expected failures in the Clinical module, with stable codes the web app relies on.</summary>
public static class ClinicalErrors
{
    public static readonly Error EncounterNotFound = Error.NotFound("encounter_not_found", "This encounter doesn't exist.");

    public static readonly Error EncounterNotOpen = Error.Conflict(
        "encounter_not_open",
        "This encounter is already closed.");
}

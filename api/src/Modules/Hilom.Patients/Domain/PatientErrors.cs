using Hilom.SharedKernel.Results;

namespace Hilom.Patients.Domain;

/// <summary>Expected failures in the Patients module, with stable codes the web app relies on.</summary>
public static class PatientErrors
{
    public static readonly Error PatientNotFound = Error.NotFound("patient_not_found", "This patient doesn't exist.");

    public static readonly Error AllergyNotFound = Error.NotFound("allergy_not_found", "This allergy entry doesn't exist.");

    public static readonly Error AllergyNotActive = Error.Conflict(
        "allergy_not_active",
        "This allergy was already resolved or marked as entered in error, so it can't be changed.");

    public static readonly Error AllergyAlreadyRecorded = Error.Conflict(
        "allergy_already_recorded",
        "This patient already has an active entry for this allergen. Edit that one instead.");

    public static Error InvalidAllergy(string field, string message) =>
        Error.Validation("invalid_allergy", message, new Dictionary<string, string[]> { [field] = [message] });
}

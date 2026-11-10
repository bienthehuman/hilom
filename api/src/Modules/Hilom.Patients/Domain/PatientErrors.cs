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

    public static readonly Error LinkNotFound = Error.NotFound("link_not_found", "This link or request doesn't exist.");

    public static readonly Error LinkNotPending = Error.Conflict(
        "link_not_pending",
        "This request was already reviewed.");

    public static readonly Error LinkNotActive = Error.Conflict(
        "link_not_active",
        "This link isn't active, so it can't be changed or revoked.");

    public static readonly Error TooManyPendingLinks = Error.Conflict(
        "too_many_pending_links",
        $"You have {PatientAccountLink.MaxPendingPerUser} requests waiting. Wait for the hospital to review them first.");

    public static readonly Error AlreadyLinked = Error.Conflict(
        "already_linked",
        "This account is already linked to this patient, or has a request for them.");

    public static readonly Error SelfLinkExists = Error.Conflict(
        "self_link_exists",
        "Another account is already linked to this patient as the patient themselves. Revoke that link first, or choose another relationship.");

    public static Error InvalidAllergy(string field, string message) =>
        Error.Validation("invalid_allergy", message, new Dictionary<string, string[]> { [field] = [message] });
}

using Hilom.SharedKernel.Results;

namespace Hilom.Patients.Domain;

/// <summary>Expected failures in the Patients module, with stable codes the web app relies on.</summary>
public static class PatientErrors
{
    public static readonly Error PatientNotFound = Error.NotFound("patient_not_found", "This patient doesn't exist.");
}

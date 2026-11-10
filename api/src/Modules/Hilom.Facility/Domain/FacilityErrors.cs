using Hilom.SharedKernel.Results;

namespace Hilom.Facility.Domain;

/// <summary>Expected failures in the Facility module, with stable codes the web app relies on.</summary>
public static class FacilityErrors
{
    public static readonly Error WardNotFound = Error.NotFound("ward_not_found", "This ward doesn't exist.");

    public static readonly Error BedNotFound = Error.NotFound("bed_not_found", "This bed doesn't exist.");

    public static readonly Error WardCodeTaken = Error.Conflict("ward_code_taken", "Another ward already has this code.");

    public static readonly Error BedLabelTaken = Error.Conflict("bed_label_taken", "This ward already has a bed with this label.");

    public static readonly Error UnknownDepartment = Error.Validation(
        "unknown_department",
        "Choose a department from the list.",
        new Dictionary<string, string[]> { ["departmentId"] = ["Choose a department from the list."] });
}

using Hilom.SharedKernel.Results;

namespace Hilom.Workforce.Domain;

/// <summary>Expected failures in the Workforce module, with stable codes the web app relies on.</summary>
public static class WorkforceErrors
{
    public static readonly Error EmployeeNotFound = Error.NotFound("employee_not_found", "This employee doesn't exist.");

    public static Error InvalidEmployee(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        Error.Validation("invalid_employee", "Some fields need attention.", fieldErrors);

    public static Error InvalidEmployee(string field, string message) =>
        InvalidEmployee(new Dictionary<string, string[]> { [field] = [message] });
}

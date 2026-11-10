using System.Globalization;
using Hilom.SharedKernel.Results;

namespace Hilom.Workforce.Domain;

/// <summary>Expected failures in the Workforce module, with stable codes the web app relies on.</summary>
public static class WorkforceErrors
{
    public static readonly Error EmployeeNotFound = Error.NotFound("employee_not_found", "This employee doesn't exist.");

    public static readonly Error EmployeeHasAccount = Error.Conflict(
        "employee_has_account",
        "This employee can already sign in.");

    public static readonly Error EmployeeSeparated = Error.BusinessRule(
        "employee_separated",
        "This employee has left the hospital. Change their status before inviting them.");

    public static readonly Error InviteAlreadyOpen = Error.Conflict(
        "invite_already_open",
        "An invite is already waiting to be accepted. Revoke it to send a new one.");

    public static readonly Error InviteNotFound = Error.NotFound(
        "invite_not_found",
        "This invite link isn't valid. Check that you copied the whole link.");

    public static readonly Error InviteNotOpen = Error.Conflict(
        "invite_not_open",
        "This invite was already accepted or revoked.");

    public static readonly Error InviteUsed = Error.BusinessRule(
        "invite_used",
        "This invite has already been used. Sign in with the password you set.");

    public static readonly Error InviteRevoked = Error.BusinessRule(
        "invite_revoked",
        "This invite was cancelled. Ask HR for a new one.");

    public static readonly Error InviteExpired = Error.BusinessRule(
        "invite_expired",
        "This invite has expired. Ask HR for a new one.");

    public static readonly Error EmailUnavailable = Error.BusinessRule(
        "email_not_sent",
        "The invite email couldn't be sent, so nothing was saved. Try again in a few minutes.");

    public static readonly Error ShiftNotFound = Error.NotFound("shift_not_found", "This shift doesn't exist.");

    public static readonly Error ShiftCodeTaken = Error.Conflict("shift_code_taken", "Another shift already has this code.");

    public static Error InvalidShift(string field, string message) =>
        Error.Validation("invalid_shift", "Some fields need attention.", new Dictionary<string, string[]> { [field] = [message] });

    public static readonly Error AssignmentNotFound = Error.NotFound("assignment_not_found", "This ward assignment doesn't exist.");

    public static readonly Error WardUnavailable = Error.Validation(
        "ward_unavailable",
        "Choose an active ward.",
        new Dictionary<string, string[]> { ["wardId"] = ["Choose an active ward."] });

    /// <summary>One ward at a time: the new period overlaps another assignment of the same employee.</summary>
    public static Error AssignmentOverlaps(string wardCode, DateOnly startsOn, DateOnly? lastDay) =>
        Error.Conflict(
            "assignment_overlaps",
            lastDay is { } last
                ? string.Create(CultureInfo.InvariantCulture, $"They're already assigned to {wardCode} from {startsOn:MMM d, yyyy} to {last:MMM d, yyyy}. Staff are on one ward at a time.")
                : string.Create(CultureInfo.InvariantCulture, $"They're already assigned to {wardCode} from {startsOn:MMM d, yyyy} with no end date. End that assignment first."));

    public static readonly Error AssignmentRaced = Error.Conflict(
        "assignment_overlaps",
        "Someone just assigned this person to a ward for overlapping dates. Reload to see it.");

    public static Error InvalidAssignment(string field, string message) =>
        Error.Validation("invalid_assignment", "Some fields need attention.", new Dictionary<string, string[]> { [field] = [message] });

    public static Error InvalidEmployee(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        Error.Validation("invalid_employee", "Some fields need attention.", fieldErrors);

    public static Error InvalidEmployee(string field, string message) =>
        InvalidEmployee(new Dictionary<string, string[]> { [field] = [message] });
}

using Hilom.Access.Domain;

namespace Hilom.DemoData;

/// <summary>The demo login for each role, in the order the sign-in page lists them, and what each lets a visitor try.</summary>
public static class DemoAccountCatalog
{
    public const string EmailDomain = "@demo.hilom.test";

    public static IReadOnlyList<(string Role, string Description)> Roles { get; } =
    [
        ("Physician", "Publish availability, confirm appointments, see visits, admit, order and discharge."),
        ("Staff Nurse", "Record vitals, notes and medications for the patients on Ward 3."),
        ("Head Nurse", "Run Ward 3: its roster, swap approvals and patients."),
        ("Chief Nurse", "Assign nurses to wards, build rosters, approve swaps, see every ward's census."),
        ("Front Desk", "Register patients, book and check in appointments, verify portal links."),
        ("HR Officer", "Hire staff, send invites, manage accounts."),
        (Role.SystemAdministratorName, "Manage users and roles, read the audit log, check its hash chain."),
        ("Patient Portal User", "Follow a parent's care: book appointments and read visit summaries."),
    ];

    public static string Describe(string role) => Roles.FirstOrDefault(r => r.Role == role).Description ?? "";

    public static int OrderOf(string role)
    {
        for (var i = 0; i < Roles.Count; i++)
        {
            if (Roles[i].Role == role)
            {
                return i;
            }
        }

        return Roles.Count;
    }
}

namespace Hilom.SharedKernel.Authorization;

// Generated from the catalog in docs/RBAC.md. A test fails if the two drift apart.
// To add a permission: add it to RBAC.md, then here, then grant it to default roles as needed.

/// <summary>Every permission in Hilom. Code checks these; roles (in the database) grant them.</summary>
public static class Perm
{
    public static class Access
    {
        public static readonly Permission UsersView = new("access.users.view", "Access", "List users", [PermissionScope.All]);

        public static readonly Permission UsersManage = new("access.users.manage", "Access", "Create, disable, unlock users; assign roles", [PermissionScope.All]);

        public static readonly Permission RolesView = new("access.roles.view", "Access", "See roles and their permissions", [PermissionScope.All]);

        public static readonly Permission RolesManage = new("access.roles.manage", "Access", "Create/edit/delete roles", [PermissionScope.All]);

        public static readonly Permission AuditView = new("access.audit.view", "Access", "Read the audit log", [PermissionScope.All]);
    }

    public static class Workforce
    {
        public static readonly Permission EmployeesView = new("workforce.employees.view", "Workforce", "See employee records (own = self)", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission EmployeesManage = new("workforce.employees.manage", "Workforce", "Create/edit employees, departments, positions, specializations", [PermissionScope.All]);

        public static readonly Permission InvitesSend = new("workforce.invites.send", "Workforce", "Send/revoke staff onboarding invites", [PermissionScope.All]);

        public static readonly Permission AssignmentsView = new("workforce.assignments.view", "Workforce", "See ward assignments", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own]);

        public static readonly Permission AssignmentsManage = new("workforce.assignments.manage", "Workforce", "Assign staff to wards", [PermissionScope.All, PermissionScope.Ward]);

        public static readonly Permission ShiftsManage = new("workforce.shifts.manage", "Workforce", "Define shift types", [PermissionScope.All]);

        public static readonly Permission RosterView = new("workforce.roster.view", "Workforce", "See the duty roster", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own]);

        public static readonly Permission RosterManage = new("workforce.roster.manage", "Workforce", "Create/edit roster entries", [PermissionScope.All, PermissionScope.Ward]);

        public static readonly Permission SwapsRequest = new("workforce.swaps.request", "Workforce", "Request a swap, and respond to swaps aimed at you", [PermissionScope.Own]);

        public static readonly Permission SwapsApprove = new("workforce.swaps.approve", "Workforce", "Approve/reject swaps that the target already accepted", [PermissionScope.All, PermissionScope.Ward]);
    }

    public static class Facility
    {
        public static readonly Permission WardsView = new("facility.wards.view", "Facility", "See wards and beds", [PermissionScope.All]);

        public static readonly Permission WardsManage = new("facility.wards.manage", "Facility", "Create/edit wards and beds", [PermissionScope.All]);

        public static readonly Permission CensusView = new("facility.census.view", "Facility", "See the live bed board", [PermissionScope.All, PermissionScope.Ward]);
    }

    public static class Patients
    {
        public static readonly Permission View = new("patients.view", "Patients", "See demographics and allergies", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own, PermissionScope.Linked]);

        public static readonly Permission Register = new("patients.register", "Patients", "Register a new patient", [PermissionScope.All]);

        public static readonly Permission Edit = new("patients.edit", "Patients", "Edit demographics", [PermissionScope.All, PermissionScope.Ward]);

        public static readonly Permission AllergiesManage = new("patients.allergies.manage", "Patients", "Add/update allergies", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own]);

        public static readonly Permission LinksRequest = new("patients.links.request", "Patients", "Portal user requests a link to a patient", [PermissionScope.Own]);

        public static readonly Permission LinksManage = new("patients.links.manage", "Patients", "Verify, reject, revoke account links; set can_book / can_view_records", [PermissionScope.All]);

        public static readonly Permission AccessHistoryView = new("patients.access_history.view", "Patients", "See who viewed or changed a patient's record (see [AUDIT.md](AUDIT.md))", [PermissionScope.All]);
    }

    public static class Scheduling
    {
        public static readonly Permission SlotsView = new("scheduling.slots.view", "Scheduling", "Browse open slots (no patient data)", [PermissionScope.All]);

        public static readonly Permission SlotsManage = new("scheduling.slots.manage", "Scheduling", "Publish/close availability slots", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission AppointmentsView = new("scheduling.appointments.view", "Scheduling", "See appointments", [PermissionScope.All, PermissionScope.Own, PermissionScope.Linked]);

        public static readonly Permission AppointmentsBook = new("scheduling.appointments.book", "Scheduling", "Book for a patient", [PermissionScope.All, PermissionScope.Linked]);

        public static readonly Permission AppointmentsDecide = new("scheduling.appointments.decide", "Scheduling", "Confirm/decline requests", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission AppointmentsCancel = new("scheduling.appointments.cancel", "Scheduling", "Cancel", [PermissionScope.All, PermissionScope.Own, PermissionScope.Linked]);

        public static readonly Permission AppointmentsCheckIn = new("scheduling.appointments.check_in", "Scheduling", "Check in, which opens an outpatient encounter", [PermissionScope.All]);
    }

    public static class Clinical
    {
        public static readonly Permission EncountersView = new("clinical.encounters.view", "Clinical", "See encounters with vitals, notes, orders, MAR", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own, PermissionScope.Linked]);

        public static readonly Permission EncountersManage = new("clinical.encounters.manage", "Clinical", "Update chief complaint/diagnosis, close encounter", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission AdmissionsAdmit = new("clinical.admissions.admit", "Clinical", "Admit a patient and assign a bed", [PermissionScope.All]);

        public static readonly Permission AdmissionsTransfer = new("clinical.admissions.transfer", "Clinical", "Move to another bed/ward (own = attending physician)", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own]);

        public static readonly Permission AdmissionsDischarge = new("clinical.admissions.discharge", "Clinical", "Discharge", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission VitalsRecord = new("clinical.vitals.record", "Clinical", "Record vitals", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own]);

        public static readonly Permission NotesWrite = new("clinical.notes.write", "Clinical", "Write clinical notes", [PermissionScope.All, PermissionScope.Ward, PermissionScope.Own]);

        public static readonly Permission OrdersWrite = new("clinical.orders.write", "Clinical", "Create physician orders", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission OrdersDiscontinue = new("clinical.orders.discontinue", "Clinical", "Discontinue orders", [PermissionScope.All, PermissionScope.Own]);

        public static readonly Permission MedicationsAdminister = new("clinical.medications.administer", "Clinical", "Record medication administration", [PermissionScope.All, PermissionScope.Ward]);

        public static readonly Permission RecordsVoid = new("clinical.records.void", "Clinical", "Void vitals/notes/MAR entered in error (own = authored by you)", [PermissionScope.All, PermissionScope.Own]);
    }

    /// <summary>The whole catalog, in display order.</summary>
    public static IReadOnlyList<Permission> All { get; } =
    [
        Access.UsersView,
        Access.UsersManage,
        Access.RolesView,
        Access.RolesManage,
        Access.AuditView,
        Workforce.EmployeesView,
        Workforce.EmployeesManage,
        Workforce.InvitesSend,
        Workforce.AssignmentsView,
        Workforce.AssignmentsManage,
        Workforce.ShiftsManage,
        Workforce.RosterView,
        Workforce.RosterManage,
        Workforce.SwapsRequest,
        Workforce.SwapsApprove,
        Facility.WardsView,
        Facility.WardsManage,
        Facility.CensusView,
        Patients.View,
        Patients.Register,
        Patients.Edit,
        Patients.AllergiesManage,
        Patients.LinksRequest,
        Patients.LinksManage,
        Patients.AccessHistoryView,
        Scheduling.SlotsView,
        Scheduling.SlotsManage,
        Scheduling.AppointmentsView,
        Scheduling.AppointmentsBook,
        Scheduling.AppointmentsDecide,
        Scheduling.AppointmentsCancel,
        Scheduling.AppointmentsCheckIn,
        Clinical.EncountersView,
        Clinical.EncountersManage,
        Clinical.AdmissionsAdmit,
        Clinical.AdmissionsTransfer,
        Clinical.AdmissionsDischarge,
        Clinical.VitalsRecord,
        Clinical.NotesWrite,
        Clinical.OrdersWrite,
        Clinical.OrdersDiscontinue,
        Clinical.MedicationsAdminister,
        Clinical.RecordsVoid,
    ];
}

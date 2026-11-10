using Hilom.Access.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Access.Seeding;

// Generated from the default-role table in docs/RBAC.md. A test fails if the two drift apart.

/// <summary>Starting roles. Seeded once; admins may edit them afterwards (except System Administrator).</summary>
public static class DefaultRoles
{
    public static IReadOnlyList<DefaultRole> All { get; } =
    [
        new(
            "HR Officer",
            "Manages staff records, onboarding invites and user accounts.",
            [
                new(Perm.Access.UsersView.Key, PermissionScope.All),
                new(Perm.Access.UsersManage.Key, PermissionScope.All),
                new(Perm.Access.RolesView.Key, PermissionScope.All),
                new(Perm.Workforce.EmployeesView.Key, PermissionScope.All),
                new(Perm.Workforce.EmployeesManage.Key, PermissionScope.All),
                new(Perm.Workforce.InvitesSend.Key, PermissionScope.All),
                new(Perm.Workforce.AssignmentsView.Key, PermissionScope.All),
                new(Perm.Workforce.RosterView.Key, PermissionScope.All),
                new(Perm.Facility.WardsView.Key, PermissionScope.All),
            ]),
        new(
            "Chief Nurse",
            "Oversees nursing across all wards: assignments, rosters, swaps and patient care.",
            [
                new(Perm.Workforce.EmployeesView.Key, PermissionScope.All),
                new(Perm.Workforce.AssignmentsView.Key, PermissionScope.All),
                new(Perm.Workforce.AssignmentsManage.Key, PermissionScope.All),
                new(Perm.Workforce.ShiftsManage.Key, PermissionScope.All),
                new(Perm.Workforce.RosterView.Key, PermissionScope.All),
                new(Perm.Workforce.RosterManage.Key, PermissionScope.All),
                new(Perm.Workforce.SwapsApprove.Key, PermissionScope.All),
                new(Perm.Facility.WardsView.Key, PermissionScope.All),
                new(Perm.Facility.WardsManage.Key, PermissionScope.All),
                new(Perm.Facility.CensusView.Key, PermissionScope.All),
                new(Perm.Patients.View.Key, PermissionScope.All),
                new(Perm.Patients.AllergiesManage.Key, PermissionScope.All),
                new(Perm.Patients.AccessHistoryView.Key, PermissionScope.All),
                new(Perm.Clinical.EncountersView.Key, PermissionScope.All),
                new(Perm.Clinical.AdmissionsTransfer.Key, PermissionScope.All),
                new(Perm.Clinical.VitalsRecord.Key, PermissionScope.All),
                new(Perm.Clinical.NotesWrite.Key, PermissionScope.All),
                new(Perm.Clinical.MedicationsAdminister.Key, PermissionScope.All),
                new(Perm.Clinical.RecordsVoid.Key, PermissionScope.All),
            ]),
        new(
            "Head Nurse",
            "Runs one ward: its roster, swap approvals and patient care.",
            [
                new(Perm.Workforce.EmployeesView.Key, PermissionScope.Own),
                new(Perm.Workforce.AssignmentsView.Key, PermissionScope.Ward),
                new(Perm.Workforce.RosterView.Key, PermissionScope.Ward),
                new(Perm.Workforce.RosterManage.Key, PermissionScope.Ward),
                new(Perm.Workforce.SwapsRequest.Key, PermissionScope.Own),
                new(Perm.Workforce.SwapsApprove.Key, PermissionScope.Ward),
                new(Perm.Facility.WardsView.Key, PermissionScope.All),
                new(Perm.Facility.CensusView.Key, PermissionScope.Ward),
                new(Perm.Patients.View.Key, PermissionScope.Ward),
                new(Perm.Patients.Edit.Key, PermissionScope.Ward),
                new(Perm.Patients.AllergiesManage.Key, PermissionScope.Ward),
                new(Perm.Clinical.EncountersView.Key, PermissionScope.Ward),
                new(Perm.Clinical.AdmissionsTransfer.Key, PermissionScope.Ward),
                new(Perm.Clinical.VitalsRecord.Key, PermissionScope.Ward),
                new(Perm.Clinical.NotesWrite.Key, PermissionScope.Ward),
                new(Perm.Clinical.MedicationsAdminister.Key, PermissionScope.Ward),
                new(Perm.Clinical.RecordsVoid.Key, PermissionScope.Own),
            ]),
        new(
            "Staff Nurse",
            "Cares for patients on assigned wards: vitals, notes and medications.",
            [
                new(Perm.Workforce.EmployeesView.Key, PermissionScope.Own),
                new(Perm.Workforce.AssignmentsView.Key, PermissionScope.Own),
                new(Perm.Workforce.RosterView.Key, PermissionScope.Ward),
                new(Perm.Workforce.SwapsRequest.Key, PermissionScope.Own),
                new(Perm.Facility.WardsView.Key, PermissionScope.All),
                new(Perm.Facility.CensusView.Key, PermissionScope.Ward),
                new(Perm.Patients.View.Key, PermissionScope.Ward),
                new(Perm.Patients.AllergiesManage.Key, PermissionScope.Ward),
                new(Perm.Clinical.EncountersView.Key, PermissionScope.Ward),
                new(Perm.Clinical.VitalsRecord.Key, PermissionScope.Ward),
                new(Perm.Clinical.NotesWrite.Key, PermissionScope.Ward),
                new(Perm.Clinical.MedicationsAdminister.Key, PermissionScope.Ward),
                new(Perm.Clinical.RecordsVoid.Key, PermissionScope.Own),
            ]),
        new(
            "Physician",
            "Sees own patients: schedules, appointments, encounters, orders and discharge.",
            [
                new(Perm.Workforce.EmployeesView.Key, PermissionScope.Own),
                new(Perm.Workforce.AssignmentsView.Key, PermissionScope.Own),
                new(Perm.Workforce.RosterView.Key, PermissionScope.Own),
                new(Perm.Facility.WardsView.Key, PermissionScope.All),
                new(Perm.Facility.CensusView.Key, PermissionScope.All),
                new(Perm.Patients.View.Key, PermissionScope.Own),
                new(Perm.Patients.AllergiesManage.Key, PermissionScope.Own),
                new(Perm.Scheduling.SlotsView.Key, PermissionScope.All),
                new(Perm.Scheduling.SlotsManage.Key, PermissionScope.Own),
                new(Perm.Scheduling.AppointmentsView.Key, PermissionScope.Own),
                new(Perm.Scheduling.AppointmentsDecide.Key, PermissionScope.Own),
                new(Perm.Scheduling.AppointmentsCancel.Key, PermissionScope.Own),
                new(Perm.Clinical.EncountersView.Key, PermissionScope.Own),
                new(Perm.Clinical.EncountersManage.Key, PermissionScope.Own),
                new(Perm.Clinical.AdmissionsAdmit.Key, PermissionScope.All),
                new(Perm.Clinical.AdmissionsTransfer.Key, PermissionScope.Own),
                new(Perm.Clinical.AdmissionsDischarge.Key, PermissionScope.Own),
                new(Perm.Clinical.VitalsRecord.Key, PermissionScope.Own),
                new(Perm.Clinical.NotesWrite.Key, PermissionScope.Own),
                new(Perm.Clinical.OrdersWrite.Key, PermissionScope.Own),
                new(Perm.Clinical.OrdersDiscontinue.Key, PermissionScope.Own),
                new(Perm.Clinical.RecordsVoid.Key, PermissionScope.Own),
            ]),
        new(
            "Front Desk",
            "Registers patients, books and checks in appointments, admits, verifies portal links.",
            [
                new(Perm.Workforce.EmployeesView.Key, PermissionScope.Own),
                new(Perm.Facility.WardsView.Key, PermissionScope.All),
                new(Perm.Facility.CensusView.Key, PermissionScope.All),
                new(Perm.Patients.View.Key, PermissionScope.All),
                new(Perm.Patients.Register.Key, PermissionScope.All),
                new(Perm.Patients.Edit.Key, PermissionScope.All),
                new(Perm.Patients.AllergiesManage.Key, PermissionScope.All),
                new(Perm.Patients.LinksManage.Key, PermissionScope.All),
                new(Perm.Scheduling.SlotsView.Key, PermissionScope.All),
                new(Perm.Scheduling.SlotsManage.Key, PermissionScope.All),
                new(Perm.Scheduling.AppointmentsView.Key, PermissionScope.All),
                new(Perm.Scheduling.AppointmentsBook.Key, PermissionScope.All),
                new(Perm.Scheduling.AppointmentsDecide.Key, PermissionScope.All),
                new(Perm.Scheduling.AppointmentsCancel.Key, PermissionScope.All),
                new(Perm.Scheduling.AppointmentsCheckIn.Key, PermissionScope.All),
                new(Perm.Clinical.AdmissionsAdmit.Key, PermissionScope.All),
            ]),
        new(
            "Patient Portal User",
            "Patient or family account. Sees only linked patients.",
            [
                new(Perm.Patients.View.Key, PermissionScope.Linked),
                new(Perm.Patients.LinksRequest.Key, PermissionScope.Own),
                new(Perm.Scheduling.SlotsView.Key, PermissionScope.All),
                new(Perm.Scheduling.AppointmentsView.Key, PermissionScope.Linked),
                new(Perm.Scheduling.AppointmentsBook.Key, PermissionScope.Linked),
                new(Perm.Scheduling.AppointmentsCancel.Key, PermissionScope.Linked),
                new(Perm.Clinical.EncountersView.Key, PermissionScope.Linked),
            ],
            IsPortal: true),
    ];
}

/// <summary>A role seeded on first start.</summary>
/// <param name="IsPortal">The role self-registered patient portal accounts get.</param>
public sealed record DefaultRole(string Name, string Description, IReadOnlyList<RoleGrant> Grants, bool IsPortal = false);

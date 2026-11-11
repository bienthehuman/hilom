namespace Hilom.SharedKernel.Auditing;

/// <summary>
/// The audit event catalog. Mirrors docs/AUDIT.md (a test fails if they drift apart). Modules add their events
/// here as they're built; the doc lists events for modules not built yet.
/// </summary>
public static class AuditEvents
{
    /// <summary>Category and key for automatic field-level change entries.</summary>
    public static readonly AuditEvent DataChanged = new("data.changed", "data", AuditSeverity.Info, "Record changed");

    public static class Auth
    {
        public static readonly AuditEvent LoginSucceeded = new("auth.login.succeeded", "auth", AuditSeverity.Info, "Signed in");
        public static readonly AuditEvent LoginFailed = new("auth.login.failed", "auth", AuditSeverity.Critical, "Sign-in failed");
        public static readonly AuditEvent AccountLockedOut = new("auth.account.locked_out", "auth", AuditSeverity.Critical, "Account locked after failed sign-ins");
        public static readonly AuditEvent Logout = new("auth.logout", "auth", AuditSeverity.Info, "Signed out");
        public static readonly AuditEvent InviteAccepted = new("auth.invite.accepted", "auth", AuditSeverity.Notice, "Invite accepted, account created");
        public static readonly AuditEvent AccountRegistered = new("auth.account.registered", "auth", AuditSeverity.Notice, "Patient portal account registered");
    }

    public static class Access
    {
        public static readonly AuditEvent UserCreated = new("access.user.created", "access", AuditSeverity.Notice, "User created");
        public static readonly AuditEvent UserDisabled = new("access.user.disabled", "access", AuditSeverity.Critical, "User disabled");
        public static readonly AuditEvent UserEnabled = new("access.user.enabled", "access", AuditSeverity.Critical, "User enabled");
        public static readonly AuditEvent UserUnlocked = new("access.user.unlocked", "access", AuditSeverity.Notice, "User unlocked");
        public static readonly AuditEvent UserRolesChanged = new("access.user.roles_changed", "access", AuditSeverity.Critical, "User's roles changed");
        public static readonly AuditEvent RoleCreated = new("access.role.created", "access", AuditSeverity.Critical, "Role created");
        public static readonly AuditEvent RoleRenamed = new("access.role.renamed", "access", AuditSeverity.Notice, "Role renamed or redescribed");
        public static readonly AuditEvent RolePermissionsChanged = new("access.role.permissions_changed", "access", AuditSeverity.Critical, "Role's permissions changed");
        public static readonly AuditEvent RoleDeleted = new("access.role.deleted", "access", AuditSeverity.Critical, "Role deleted");
        public static readonly AuditEvent Denied = new("access.denied", "access", AuditSeverity.Critical, "Access denied");
        public static readonly AuditEvent AuditViewed = new("access.audit.viewed", "access", AuditSeverity.Info, "Audit log viewed");
    }

    public static class Workforce
    {
        public static readonly AuditEvent EmployeeCreated = new("workforce.employee.created", "workforce", AuditSeverity.Notice, "Employee created");
        public static readonly AuditEvent EmployeeStatusChanged = new("workforce.employee.status_changed", "workforce", AuditSeverity.Critical, "Employee's status changed");
        public static readonly AuditEvent InviteSent = new("workforce.invite.sent", "workforce", AuditSeverity.Notice, "Staff invite sent");
        public static readonly AuditEvent InviteRevoked = new("workforce.invite.revoked", "workforce", AuditSeverity.Notice, "Staff invite revoked");
        public static readonly AuditEvent RosterChanged = new("workforce.roster.changed", "workforce", AuditSeverity.Info, "Duty roster changed");
        public static readonly AuditEvent SwapRequested = new("workforce.swap.requested", "workforce", AuditSeverity.Info, "Shift swap requested");
        public static readonly AuditEvent SwapAccepted = new("workforce.swap.accepted", "workforce", AuditSeverity.Info, "Shift swap accepted by colleague");
        public static readonly AuditEvent SwapDeclined = new("workforce.swap.declined", "workforce", AuditSeverity.Info, "Shift swap declined by colleague");
        public static readonly AuditEvent SwapCancelled = new("workforce.swap.cancelled", "workforce", AuditSeverity.Info, "Shift swap cancelled by requester");
        public static readonly AuditEvent SwapApproved = new("workforce.swap.approved", "workforce", AuditSeverity.Notice, "Shift swap approved");
        public static readonly AuditEvent SwapRejected = new("workforce.swap.rejected", "workforce", AuditSeverity.Notice, "Shift swap rejected");
        public static readonly AuditEvent WardAssignmentChanged = new("workforce.ward_assignment.changed", "workforce", AuditSeverity.Critical, "Ward assignment changed");
    }

    public static class Patients
    {
        public static readonly AuditEvent PatientRegistered = new("patients.patient.registered", "patients", AuditSeverity.Notice, "Patient registered");
        public static readonly AuditEvent DemographicsChanged = new("patients.patient.demographics_changed", "patients", AuditSeverity.Notice, "Patient's demographics changed");
        public static readonly AuditEvent RecordViewed = new("patients.record.viewed", "patients", AuditSeverity.Info, "Patient record opened");
        public static readonly AuditEvent AllergyRecorded = new("patients.allergy.recorded", "patients", AuditSeverity.Notice, "Allergy recorded");
        public static readonly AuditEvent LinkRequested = new("patients.link.requested", "patients", AuditSeverity.Info, "Portal account asked to be linked to a patient");
        public static readonly AuditEvent LinkVerified = new("patients.link.verified", "patients", AuditSeverity.Critical, "Patient link verified");
        public static readonly AuditEvent LinkRejected = new("patients.link.rejected", "patients", AuditSeverity.Critical, "Patient link request rejected");
        public static readonly AuditEvent LinkRevoked = new("patients.link.revoked", "patients", AuditSeverity.Critical, "Patient link revoked");
        public static readonly AuditEvent LinkPermissionsChanged = new("patients.link.flags_changed", "patients", AuditSeverity.Critical, "Patient link's booking or record access changed");
        public static readonly AuditEvent AllergyChanged = new("patients.allergy.changed", "patients", AuditSeverity.Notice, "Allergy edited, resolved or marked entered in error");
    }

    public static class Scheduling
    {
        public static readonly AuditEvent SlotsPublished = new("scheduling.slot.published", "scheduling", AuditSeverity.Info, "Availability slots published");
        public static readonly AuditEvent AppointmentBooked = new("scheduling.appointment.booked", "scheduling", AuditSeverity.Info, "Appointment booked");
        public static readonly AuditEvent AppointmentConfirmed = new("scheduling.appointment.confirmed", "scheduling", AuditSeverity.Info, "Appointment confirmed");
        public static readonly AuditEvent AppointmentDeclined = new("scheduling.appointment.declined", "scheduling", AuditSeverity.Info, "Appointment request declined");
        public static readonly AuditEvent AppointmentCancelled = new("scheduling.appointment.cancelled", "scheduling", AuditSeverity.Notice, "Appointment cancelled");
        public static readonly AuditEvent AppointmentNoShow = new("scheduling.appointment.no_show", "scheduling", AuditSeverity.Info, "Patient didn't come to the appointment");
        public static readonly AuditEvent SlotClosed = new("scheduling.slot.closed", "scheduling", AuditSeverity.Info, "Availability slot closed");
        public static readonly AuditEvent AppointmentCheckedIn = new("scheduling.appointment.checked_in", "scheduling", AuditSeverity.Info, "Patient checked in for the appointment");
    }

    public static class Clinical
    {
        public static readonly AuditEvent EncounterOpened = new("clinical.encounter.opened", "clinical", AuditSeverity.Notice, "Encounter opened");
        public static readonly AuditEvent EncounterChanged = new("clinical.encounter.changed", "clinical", AuditSeverity.Notice, "Encounter's chief complaint or diagnosis summary changed");
        public static readonly AuditEvent EncounterClosed = new("clinical.encounter.closed", "clinical", AuditSeverity.Notice, "Encounter closed");
        public static readonly AuditEvent EncounterViewed = new("clinical.encounter.viewed", "clinical", AuditSeverity.Info, "Encounter viewed");
        public static readonly AuditEvent VitalsRecorded = new("clinical.vitals.recorded", "clinical", AuditSeverity.Info, "Vital signs recorded");
        public static readonly AuditEvent NoteWritten = new("clinical.note.written", "clinical", AuditSeverity.Info, "Clinical note written");
        public static readonly AuditEvent NoteChanged = new("clinical.note.changed", "clinical", AuditSeverity.Info, "Clinical note edited within its 24-hour window");
        public static readonly AuditEvent RecordVoided = new("clinical.record.voided", "clinical", AuditSeverity.Critical, "Clinical entry voided as entered in error");
    }

    public static class Facility
    {
        public static readonly AuditEvent WardChanged = new("facility.ward.changed", "facility", AuditSeverity.Notice, "Ward created, deactivated or reactivated");
        public static readonly AuditEvent BedChanged = new("facility.bed.changed", "facility", AuditSeverity.Notice, "Bed added, deactivated or reactivated");
    }

    public static IReadOnlyList<AuditEvent> All { get; } =
    [
        DataChanged,
        Auth.LoginSucceeded,
        Auth.LoginFailed,
        Auth.AccountLockedOut,
        Auth.Logout,
        Auth.InviteAccepted,
        Auth.AccountRegistered,
        Access.UserCreated,
        Access.UserDisabled,
        Access.UserEnabled,
        Access.UserUnlocked,
        Access.UserRolesChanged,
        Access.RoleCreated,
        Access.RoleRenamed,
        Access.RolePermissionsChanged,
        Access.RoleDeleted,
        Access.Denied,
        Access.AuditViewed,
        Workforce.EmployeeCreated,
        Workforce.EmployeeStatusChanged,
        Workforce.InviteSent,
        Workforce.InviteRevoked,
        Workforce.WardAssignmentChanged,
        Workforce.RosterChanged,
        Workforce.SwapRequested,
        Workforce.SwapAccepted,
        Workforce.SwapDeclined,
        Workforce.SwapCancelled,
        Workforce.SwapApproved,
        Workforce.SwapRejected,
        Facility.WardChanged,
        Facility.BedChanged,
        Patients.PatientRegistered,
        Patients.DemographicsChanged,
        Patients.RecordViewed,
        Patients.AllergyRecorded,
        Patients.AllergyChanged,
        Patients.LinkRequested,
        Patients.LinkVerified,
        Patients.LinkRejected,
        Patients.LinkRevoked,
        Patients.LinkPermissionsChanged,
        Scheduling.SlotsPublished,
        Scheduling.SlotClosed,
        Scheduling.AppointmentBooked,
        Scheduling.AppointmentConfirmed,
        Scheduling.AppointmentDeclined,
        Scheduling.AppointmentCancelled,
        Scheduling.AppointmentNoShow,
        Scheduling.AppointmentCheckedIn,
        Clinical.EncounterOpened,
        Clinical.EncounterChanged,
        Clinical.EncounterClosed,
        Clinical.EncounterViewed,
        Clinical.VitalsRecorded,
        Clinical.NoteWritten,
        Clinical.NoteChanged,
        Clinical.RecordVoided,
    ];
}

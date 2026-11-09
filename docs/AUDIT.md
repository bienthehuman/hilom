# Audit Logging

Every critical action in the system leaves a permanent record of **who did what, to which record, for which patient, when, from where, and whether it worked.**

See [ADR 0007](adr/0007-audit-critical-events.md) for the reasoning.

## Three kinds of audit entries

| Kind | How it's written | Example |
|---|---|---|
| **Named event** | Declared on the endpoint and written in the same transaction as the change | `clinical.patient.transferred` with from/to ward and bed |
| **Data change** | Automatic, from an EF `SaveChanges` interceptor: field-level before/after | `patients.contact_number: ["0917…", "0918…"]` |
| **Sensitive read** | Written when someone opens a patient's record | `patient.record.viewed` by Nurse Reyes |

A named event and its data changes share a `correlation_id`. The audit viewer shows "Patient transferred" with the exact field diffs underneath.

## Rules

1. **Every state-changing endpoint declares its audit event**, e.g. `.Audited(AuditEvent.Clinical.PatientTransferred)`, or explicitly opts out with `.NotAudited("reason")`. **A test fails the build if any `POST`/`PUT`/`PATCH`/`DELETE` endpoint has neither**, the same way permissions are enforced.
2. **Events are written in the same database transaction as the change.** If the change commits, the audit row commits. If one fails, neither is saved.
3. **Failed and denied attempts are logged too**: failed logins, lockouts, and requests rejected by permission or scope checks (`outcome = denied`).
4. **Sensitive reads are logged once per user per patient per 15 minutes.** Opening a chart five times in a minute is one entry, not five.
5. **The log is append-only.** The app's database role has `INSERT` and `SELECT` only. Nobody can edit or delete an entry, including System Administrators.
6. **Tamper evidence:** a background job links entries into a hash chain every minute (`hash = sha256(prev_hash ‖ entry)`). A verify endpoint reports the first broken link if any row was altered directly in the database. (Phase 4.)
7. **No secrets or full clinical text in the log.** Passwords, tokens and note bodies are never logged. Note edits record that the note changed, not its contents.

## Severity

| Severity | Meaning | Shown |
|---|---|---|
| `critical` | Security-relevant or hard to reverse | Highlighted in the audit viewer. Count shown on the admin dashboard for the last 24h. |
| `notice` | Important business event | Normal |
| `info` | Routine change or read | Normal, hidden by default filter |

## Event catalog

Events are a static catalog in code, like permissions. Each event has a fixed severity.

### Authentication
| Event | Severity |
|---|---|
| `auth.login.succeeded` | info |
| `auth.login.failed` | critical (records attempted email; `user_id` empty if unknown) |
| `auth.account.locked_out` | critical |
| `auth.logout` | info |
| `auth.password.changed` | notice |
| `auth.password.reset` | critical |
| `auth.invite.accepted` | notice |

### Access control
| Event | Severity |
|---|---|
| `access.user.created` | notice |
| `access.user.disabled` / `access.user.enabled` | critical |
| `access.user.roles_changed` | critical (before/after role list) |
| `access.role.created` / `access.role.deleted` | critical |
| `access.role.permissions_changed` | critical (before/after permission + scope list) |
| `access.denied` | critical (permission or scope rejection; includes the permission and target) |

### Patients
| Event | Severity |
|---|---|
| `patient.registered` | notice |
| `patient.demographics.changed` | notice |
| `patient.allergy.recorded` / `patient.allergy.changed` | notice |
| `patient.link.requested` | info |
| `patient.link.verified` / `patient.link.rejected` | critical |
| `patient.link.revoked` | critical |
| `patient.link.flags_changed` | critical (`can_book` / `can_view_records`) |
| `patient.record.viewed` | info (sensitive read) |

### Scheduling
| Event | Severity |
|---|---|
| `appointment.booked` | info (records who booked, and whether it was for a linked patient) |
| `appointment.confirmed` / `appointment.declined` | info |
| `appointment.cancelled` | notice |
| `appointment.checked_in` / `appointment.no_show` | info |
| `slot.published` / `slot.closed` | info |

### Clinical
| Event | Severity |
|---|---|
| `encounter.opened` / `encounter.closed` | notice |
| `encounter.viewed` | info (sensitive read) |
| `clinical.patient.admitted` | notice (ward, bed, attending) |
| `clinical.patient.transferred` | notice (from/to ward and bed, reason) |
| `clinical.patient.discharged` | notice (disposition) |
| `clinical.vitals.recorded` | info |
| `clinical.note.written` | info |
| `clinical.order.created` / `clinical.order.discontinued` | notice |
| `clinical.medication.administered` | notice (outcome: given / held / refused / not available) |
| `clinical.record.voided` | critical (what was voided, reason) |

### Workforce and facility
| Event | Severity |
|---|---|
| `workforce.employee.created` | notice |
| `workforce.employee.status_changed` | critical (e.g. separated) |
| `workforce.invite.sent` / `workforce.invite.revoked` | notice |
| `workforce.ward_assignment.changed` | critical (this changes who can see which patients) |
| `workforce.roster.changed` | info |
| `workforce.swap.requested` / `accepted` / `declined` | info |
| `workforce.swap.approved` / `rejected` | notice |
| `facility.ward.changed` / `facility.bed.changed` | notice (created, deactivated) |

Ward assignments and patient links are **critical** because each one silently grants or removes access to patient records.

## Who can see the log

| Permission | Scope | Default roles | Shows |
|---|---|---|---|
| `access.audit.view` | all | System Administrator | The full log, with filters |
| `patients.access_history.view` | all | System Administrator, Chief Nurse | The "Access history" tab on a patient: every view and change of that patient's records |

Viewing the audit log is itself logged (`access.audit.viewed`, info).

## Screens

- **Audit log** (admin). Filters: date range, user, patient, event, category, severity, outcome. Expanding an entry shows its field diffs and related entries (same `correlation_id`). Export to CSV, which is itself logged as `access.audit.exported` (critical).
- **Patient → Access history.** Answers "who opened or changed this patient's record, and when."
- **Admin dashboard tile:** critical events in the last 24 hours, linking into the filtered log.

## Retention

Kept forever by default (it's a demo-scale database). Indexes on `(occurred_at)`, `(user_id, occurred_at)`, `(patient_id, occurred_at)` and `(event, occurred_at)`. If volume ever matters, the table can move to monthly partitions without changing the app.

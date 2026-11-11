# Audit Logging

Every critical action in the system leaves a permanent record of **who did what, to which record, for which patient, when, from where, and whether it worked.**

See [ADR 0007](adr/0007-audit-critical-events.md) for the reasoning.

## Three kinds of audit entries

| Kind | How it's written | Example |
|---|---|---|
| **Named event** | Declared on the endpoint and written in the same transaction as the change | `clinical.patient.transferred` with from/to ward and bed |
| **Data change** | Automatic, from an EF `SaveChanges` interceptor: field-level before/after | `patients.contact_number: ["0917…", "0918…"]` |
| **Sensitive read** | Written when someone opens a patient's record | `patients.record.viewed` by Nurse Reyes |

A named event and its data changes share a `correlation_id`. The audit viewer shows "Patient transferred" with the exact field diffs underneath.

## How an endpoint records an event

```csharp
group.MapPost("/roles", CreateRole)
    .RequirePermission(Perm.Access.RolesManage)
    .Audited(AuditEvents.Access.RoleCreated);

// in the handler, before SaveChangesAsync, so both commit together:
auditLog.Record(AuditEvents.Access.RoleCreated, new AuditOptions { Target = new AuditTarget(nameof(Role), role.Id) });
```

- `.Audited(...)` lists the events the endpoint may record. Outside production, a successful response that recorded none of them throws, so a forgotten `Record` call fails the tests.
- When a request succeeds but changes nothing (e.g. saving a role with the same permissions), the handler calls `auditLog.NothingToRecord()`.
- `RecordNowAsync` writes immediately in its own transaction. It's for events that must survive even when the request fails or changes nothing else: sign-ins and `access.denied`.
- Properties marked `[SensitiveData]` (password hash, security stamp) show as `"[redacted]"` in data changes.

## Rules

1. **Every state-changing endpoint declares its audit event**, e.g. `.Audited(AuditEvent.Clinical.PatientTransferred)`, or explicitly opts out with `.NotAudited("reason")`. **A test fails the build if any `POST`/`PUT`/`PATCH`/`DELETE` endpoint has neither**, the same way permissions are enforced.
2. **Events are written in the same database transaction as the change.** If the change commits, the audit row commits. If one fails, neither is saved.
3. **Failed and denied attempts are logged too**: failed logins, lockouts, and requests rejected by permission or scope checks (`outcome = denied`).
4. **Sensitive reads are logged once per user per patient per 15 minutes.** Opening a chart five times in a minute is one entry, not five.
5. **The log is append-only.** A database trigger rejects every `UPDATE`, `DELETE` and `TRUNCATE` on `access.audit_log`, even from the table's owner. The only update allowed is filling the sealing columns (`prev_hash`, `hash`, `sealed_at`) once, on a row that isn't sealed yet. Nobody can edit or delete an entry, including System Administrators. In deployment the app also connects as a role with only `INSERT` and `SELECT` on the table (Phase 4).
6. **Tamper evidence:** a background job links entries into a hash chain every minute (`hash = sha256(prev_hash ‖ entry)`). A verify endpoint reports the first broken link if any row was altered directly in the database. (Phase 4.)
7. **No secrets or full clinical text in the log.** Passwords, tokens and note bodies are never logged. Note edits record that the note changed, not its contents.

## Severity

| Severity | Meaning | Shown |
|---|---|---|
| `critical` | Security-relevant or hard to reverse | Highlighted in the audit viewer. Count shown on the admin dashboard for the last 24h. |
| `notice` | Important business event | Normal |
| `info` | Routine change or read | Normal, hidden by default filter |

## Event catalog

Events are a static catalog in code, like permissions. Each event has a fixed severity. Keys read `<category>.<thing>.<action>`, and the category is the first part.

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
| `auth.account.registered` | notice (a patient or family member created a portal account) |

### Access control
| Event | Severity |
|---|---|
| `access.user.created` | notice |
| `access.user.disabled` / `access.user.enabled` | critical |
| `access.user.unlocked` | notice (cleared a lockout early) |
| `access.user.roles_changed` | critical (before/after role list) |
| `access.role.created` / `access.role.deleted` | critical |
| `access.role.renamed` | notice (name or description changed; permission changes are logged separately) |
| `access.role.permissions_changed` | critical (before/after permission + scope list) |
| `access.denied` | critical (permission or scope rejection; includes the permission and target) |

### Patients
| Event | Severity |
|---|---|
| `patients.patient.registered` | notice |
| `patients.patient.demographics_changed` | notice |
| `patients.allergy.recorded` / `patients.allergy.changed` | notice |
| `patients.link.requested` | info |
| `patients.link.verified` / `patients.link.rejected` | critical |
| `patients.link.revoked` | critical |
| `patients.link.flags_changed` | critical (`can_book` / `can_view_records`) |
| `patients.record.viewed` | info (sensitive read) |

### Scheduling
| Event | Severity |
|---|---|
| `scheduling.appointment.booked` | info (records who booked, and whether it was for a linked patient) |
| `scheduling.appointment.confirmed` / `scheduling.appointment.declined` | info |
| `scheduling.appointment.cancelled` | notice |
| `scheduling.appointment.checked_in` / `scheduling.appointment.no_show` | info |
| `scheduling.slot.published` / `scheduling.slot.closed` | info |

### Clinical
| Event | Severity |
|---|---|
| `clinical.encounter.opened` / `clinical.encounter.closed` | notice |
| `clinical.encounter.changed` | notice (which fields: chief complaint, diagnosis summary; never the text) |
| `clinical.encounter.viewed` | info (sensitive read) |
| `clinical.patient.admitted` | notice (ward, bed, attending) |
| `clinical.patient.transferred` | notice (from/to ward and bed, reason) |
| `clinical.patient.discharged` | notice (disposition) |
| `clinical.vitals.recorded` | info |
| `clinical.note.written` | info |
| `clinical.note.changed` | info (which fields: type, body; never the text) |
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
| `workforce.swap.requested` / `accepted` / `declined` / `cancelled` | info |
| `workforce.swap.approved` / `rejected` | notice |
| `facility.ward.changed` / `facility.bed.changed` | notice (created, deactivated) |

Reference lists (departments, positions, specializations) have no named event. Their endpoints are `.NotAudited(...)`, and every add, rename, deactivation and reactivation is still logged as a data change with before/after values.

Ward assignments and patient links are **critical** because each one silently grants or removes access to patient records.

## Who can see the log

| Permission | Scope | Default roles | Shows |
|---|---|---|---|
| `access.audit.view` | all | System Administrator | The full log, with filters |
| `patients.access_history.view` | all | System Administrator, Chief Nurse | The "Access history" tab on a patient: every view and change of that patient's records |

Viewing the audit log is itself logged (`access.audit.viewed`, info).

## Screens

- **Audit log** (admin). Filters: date range, user, patient, event, category, severity, outcome. Expanding an entry shows its field diffs and related entries (same `correlation_id`). Export to CSV, which is itself logged as `access.audit.exported` (critical).
  - *Built in 0.9:* filters for date range (hospital time), person, event, severity and kind (events, data changes or both), a details panel with field diffs, and "show everything from this request". The patient filter comes with the patient access history; CSV export and the dashboard tile come in Phase 4.
  - *Built in 2.4:* a portal user's record view also records the link they went through (`acting_for_patient_link_id`).
  - *Built in 3.6:* `clinical.patient.transferred` names the from and to ward codes and bed labels, and the transfer reason. `clinical.patient.discharged` names the disposition, the last ward and bed, and the summary note's id; it's recorded with `clinical.note.written` (type `discharge_summary`) and `clinical.encounter.closed` under one correlation id. The summary text shows as `[redacted]`, like every note body.
  - *Built in 3.5:* the ward board isn't logged as a record view. Like the encounter and patient lists, it's a list: opening a stay from a tile is the sensitive read (`clinical.encounter.viewed`). Asking for a ward outside your census scope is logged as `access.denied`.
  - *Built in 3.4:* admitting records `clinical.encounter.opened` and `clinical.patient.admitted` together. The admitted event names the ward code, bed label and attending physician's employee number. The admitting diagnosis and reason are clinical text, so data-change entries show them as `[redacted]`.
  - *Built in 3.9:* a portal user reading a linked patient's records (`GET /api/clinical/portal-records/{patientId}`) is a `clinical.encounter.viewed` entry (same 15-minute rule) on the patient, with `acting_for_patient_link_id` set and `via: portal` in the details. Asking for records without an active `can_view_records` link is `access.denied` (`out_of_scope`).
  - *Built in 3.8:* `clinical.medication.administered` (notice) names the encounter, the order, the medication, the outcome, whether an allergy check was confirmed, and the allergens that matched the medication by name. The notes (the reason a dose wasn't given) are clinical text and show as `[redacted]` in data changes. Voiding a dose is `clinical.record.voided` with `recordType: medication_administration`. Reading the MAR counts as viewing the encounter (`clinical.encounter.viewed`, same 15-minute rule).
  - *Built in 3.7:* `clinical.order.created` names the order type and, for a medication, its name; `clinical.order.discontinued` adds the reason, which is the point of the entry. The order's `details` text shows as `[redacted]` in data changes, like note bodies. Listing orders counts as viewing the encounter (`clinical.encounter.viewed`, same 15-minute rule).
  - *Built in 3.3:* `clinical.note.written` names the note type (and the note it replaces, for a correction); `clinical.note.changed` names the fields the author edited within 24 hours. Note bodies show as `[redacted]` in data changes. `clinical.record.voided` (critical) is the one void event for vitals, notes and MAR: it names the kind of entry, the encounter and the reason. Voiding an entry outside your void scope is a 404 logged as `access.denied`.
  - *Built in 3.2:* `clinical.vitals.recorded` names which measurements were taken and which were in a critical range. The values themselves are in the data-change entry; the free-text notes show as `[redacted]`. Listing an encounter's vitals counts as viewing the encounter (`clinical.encounter.viewed`, same 15-minute rule).
  - *Built in 3.1:* check-in records `scheduling.appointment.checked_in` and `clinical.encounter.opened` together. Opening an encounter is a sensitive read (`clinical.encounter.viewed`, same 15-minute rule). An encounter's chief complaint and diagnosis summary are clinical text, so data-change entries show them as `[redacted]`.
  - *Built in 2.1:* patient record views (`patients.record.viewed`, logged once per user per patient per 15 minutes) and patient registration and demographics events. Every entry about a patient, data changes included, carries `patient_id` for the access history.
- **Patient → Access history.** Answers "who opened or changed this patient's record, and when."
- **Admin dashboard tile:** critical events in the last 24 hours, linking into the filtered log.

## Retention

Kept forever by default (it's a demo-scale database). Indexes on `(occurred_at)`, `(user_id, occurred_at)`, `(patient_id, occurred_at)` and `(event, occurred_at)`. If volume ever matters, the table can move to monthly partitions without changing the app.

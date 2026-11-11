# Access Control (RBAC)

## The model in one line

**A role grants permissions. Each permission comes with a scope. Code only ever asks "does this user have permission X, and does this record fall within their scope?"**

No code checks a role name. Roles are rows in the database, and admins build them in the UI.

| Piece | Lives in | Who changes it |
|---|---|---|
| **Permission**: an action, e.g. `clinical.vitals.record` | Code (a static catalog) | Developers, because code has to enforce it |
| **Scope**: which records, e.g. `ward` | Code (catalog lists allowed scopes per permission) | Developers |
| **Role**: a named bundle of permission + scope pairs | Database | Admins, in the UI |
| **User ↔ role** | Database | Admins, in the UI |

See [ADR 0002](adr/0002-rbac-permissions-in-code-roles-as-data.md).

## Scopes

| Scope | Meaning | Resolved from |
|---|---|---|
| `all` | Every record | — |
| `ward` | Records tied to a ward the user is assigned to **today** | `workforce.ward_assignments` (period contains today) → patients with an open `bed_assignment` in that ward; roster/assignments of that ward |
| `own` | Records the user is directly responsible for | Doctors: encounters where they are attending, slots/appointments they own. Staff: their own employee record, roster entries, and swaps. Authored notes/vitals for voiding. |
| `linked` | Patients the user has an active, verified account link to | `patients.patient_account_links` (status `active`). Record-viewing also needs `can_view_records`, and booking needs `can_book`. |

**Rules**
- A user with several roles gets the **widest** scope across them (`all` > `ward` > `own` / `linked`; `ward`, `own` and `linked` combine as a union).
- Scope is **evaluated live**. When a patient moves wards, or a nurse is reassigned, access follows the next request automatically. No permission edits are needed.
- **Patients are reached through other records.** For patients, `ward` means an open bed assignment on the user's ward, `own` means the physician's appointments and encounters, and `linked` means an active account link. Until the slice that builds each of those records (phase 3 for `ward` and for encounters), that scope reaches no patients. Since 2.6, a physician's `own` patients are those with an appointment in one of their slots (any status except declined or cancelled). Modules above Patients add these through `IPatientReachSource`; Patients combines them with links in `PatientReach`. For `scheduling.slots.manage`, `own` means the physician's own slots (2.5): their employee record, through `IStaffScope`. `linked` is built (2.4): for `patients.view` it reaches patients with an active link that has `can_view_records`. A link's patient name and MRN are shown to the account holder in the portal even without `can_view_records`, so they can book.
- **Decided:** `ward` is based on assignment, not on whether the nurse is on shift right now. Tying it to the shift is stricter, but breaks handover and late charting.

## Permission catalog

Scopes shown are the ones the catalog **allows**. The role decides which one it gets.

### Access
| Permission | Scopes | Allows |
|---|---|---|
| `access.users.view` | all | List users |
| `access.users.manage` | all | Create, disable, unlock users; assign roles |
| `access.roles.view` | all | See roles and their permissions |
| `access.roles.manage` | all | Create/edit/delete roles |
| `access.audit.view` | all | Read the audit log |

### Workforce
| Permission | Scopes | Allows |
|---|---|---|
| `workforce.employees.view` | all, own | See employee records (`own` = self) |
| `workforce.employees.manage` | all | Create/edit employees, departments, positions, specializations |
| `workforce.invites.send` | all | Send/revoke staff onboarding invites |
| `workforce.assignments.view` | all, ward, own | See ward assignments |
| `workforce.assignments.manage` | all, ward | Assign staff to wards |
| `workforce.shifts.manage` | all | Define shift types |
| `workforce.roster.view` | all, ward, own | See the duty roster |
| `workforce.roster.manage` | all, ward | Create/edit roster entries |
| `workforce.swaps.request` | own | Request a swap, and respond to swaps aimed at you |
| `workforce.swaps.approve` | all, ward | Approve/reject swaps that the target already accepted |

### Facility
| Permission | Scopes | Allows |
|---|---|---|
| `facility.wards.view` | all | See wards and beds |
| `facility.wards.manage` | all | Create/edit wards and beds |
| `facility.census.view` | all, ward | See the live bed board |

### Patients
| Permission | Scopes | Allows |
|---|---|---|
| `patients.view` | all, ward, own, linked | See demographics and allergies |
| `patients.register` | all | Register a new patient |
| `patients.edit` | all, ward | Edit demographics |
| `patients.allergies.manage` | all, ward, own | Add/update allergies |
| `patients.links.request` | own | Portal user requests a link to a patient |
| `patients.links.manage` | all | Verify, reject, revoke account links; set `can_book` / `can_view_records` |
| `patients.access_history.view` | all | See who viewed or changed a patient's record (see [AUDIT.md](AUDIT.md)) |

### Scheduling
| Permission | Scopes | Allows |
|---|---|---|
| `scheduling.slots.view` | all | Browse open slots (no patient data) |
| `scheduling.slots.manage` | all, own | Publish/close availability slots |
| `scheduling.appointments.view` | all, own, linked | See appointments |
| `scheduling.appointments.book` | all, linked | Book for a patient |
| `scheduling.appointments.decide` | all, own | Confirm/decline requests |
| `scheduling.appointments.cancel` | all, own, linked | Cancel |
| `scheduling.appointments.check_in` | all | Check in, which opens an outpatient encounter |

### Clinical
| Permission | Scopes | Allows |
|---|---|---|
| `clinical.encounters.view` | all, ward, own, linked | See encounters with vitals, notes, orders, MAR |
| `clinical.encounters.manage` | all, own | Update chief complaint/diagnosis, close encounter |
| `clinical.admissions.admit` | all | Admit a patient and assign a bed |
| `clinical.admissions.transfer` | all, ward, own | Move to another bed/ward (`own` = attending physician) |
| `clinical.admissions.discharge` | all, own | Discharge |
| `clinical.vitals.record` | all, ward, own | Record vitals |
| `clinical.notes.write` | all, ward, own | Write clinical notes |
| `clinical.orders.write` | all, own | Create physician orders |
| `clinical.orders.discontinue` | all, own | Discontinue orders |
| `clinical.medications.administer` | all, ward | Record medication administration |
| `clinical.records.void` | all, own | Void vitals/notes/MAR entered in error (`own` = authored by you) |

`linked` on `clinical.encounters.view` returns a **portal-safe view**: diagnosis summary, discharge summary, vitals and appointments. It doesn't include nursing notes or the full MAR. **Decided.**

## Default roles (seeded, fully editable except System Administrator)

Key: **A** = all · **W** = ward · **O** = own · **L** = linked · blank = not granted

| Permission | Sys Admin | HR | Chief Nurse | Head Nurse | Staff Nurse | Physician | Front Desk | Portal User |
|---|---|---|---|---|---|---|---|---|
| access.users.view | A | A | | | | | | |
| access.users.manage | A | A | | | | | | |
| access.roles.view | A | A | | | | | | |
| access.roles.manage | A | | | | | | | |
| access.audit.view | A | | | | | | | |
| workforce.employees.view | A | A | A | O | O | O | O | |
| workforce.employees.manage | A | A | | | | | | |
| workforce.invites.send | A | A | | | | | | |
| workforce.assignments.view | A | A | A | W | O | O | | |
| workforce.assignments.manage | A | | A | | | | | |
| workforce.shifts.manage | A | | A | | | | | |
| workforce.roster.view | A | A | A | W | W | O | | |
| workforce.roster.manage | A | | A | W | | | | |
| workforce.swaps.request | | | | O | O | | | |
| workforce.swaps.approve | A | | A | W | | | | |
| facility.wards.view | A | A | A | A | A | A | A | |
| facility.wards.manage | A | | A | | | | | |
| facility.census.view | A | | A | W | W | A | A | |
| patients.view | A | | A | W | W | O | A | L |
| patients.register | A | | | | | | A | |
| patients.edit | A | | | W | | | A | |
| patients.allergies.manage | A | | A | W | W | O | A | |
| patients.links.request | | | | | | | | O |
| patients.links.manage | A | | | | | | A | |
| patients.access_history.view | A | | A | | | | | |
| scheduling.slots.view | A | | | | | A | A | A |
| scheduling.slots.manage | A | | | | | O | A | |
| scheduling.appointments.view | A | | | | | O | A | L |
| scheduling.appointments.book | A | | | | | | A | L |
| scheduling.appointments.decide | A | | | | | O | A | |
| scheduling.appointments.cancel | A | | | | | O | A | L |
| scheduling.appointments.check_in | A | | | | | | A | |
| clinical.encounters.view | A | | A | W | W | O | | L |
| clinical.encounters.manage | A | | | | | O | | |
| clinical.admissions.admit | A | | | | | A | A | |
| clinical.admissions.transfer | A | | A | W | | O | | |
| clinical.admissions.discharge | A | | | | | O | | |
| clinical.vitals.record | A | | A | W | W | O | | |
| clinical.notes.write | A | | A | W | W | O | | |
| clinical.orders.write | A | | | | | O | | |
| clinical.orders.discontinue | A | | | | | O | | |
| clinical.medications.administer | A | | A | W | W | | | |
| clinical.records.void | A | | A | O | O | O | | |

**Notes**
- **Physician `patients.view = own`** means patients they've had an encounter or appointment with. Without that, a doctor couldn't open a patient's chart before the encounter starts. **Decided:** keep `own`. Admins can widen it to `all` in the Roles UI without code changes.
- **Sys Admin has no swap request.** It isn't a staff role. Admins who are also nurses get both roles.
- **Marking a no-show uses `scheduling.appointments.check_in`.** Like check-in, it's the front desk recording whether the patient arrived, so no separate permission. **Decided** in 2.7.
- **Who reaches an appointment:** `all`; `own` = appointments in the physician's own slots; `linked` = appointments of linked patients (seeing them needs an active link, cancelling needs `can_book`).
- **Front Desk can't see clinical encounters.** They handle registration, scheduling and admitting only.
- **Who reaches an encounter** (built in 3.1): `all`; `own` = encounters where the physician is the attending physician. A physician's `own` patients now also include everyone they're attending for. `ward` (patients in a bed on my wards today) comes with admissions in 3.4, and `linked` (the portal-safe view) in 3.9; until then they reach no encounters. `clinical.encounters.manage` uses the same rule.
- **Recording vitals** (built in 3.2) uses the same encounter reach for `clinical.vitals.record`: `all`, or `own` = encounters the physician attends. `ward` arrives with admissions (3.4). Whoever records needs an employee record, since vitals name the employee who took them (`no_employee_record` otherwise).
- **Notes and voiding** (built in 3.3): `clinical.notes.write` uses the encounter reach, like vitals, and needs an employee record. Only a note's author edits it, within 24 hours (`not_note_author`, 403, otherwise). `clinical.records.void` is checked per entry: `all`, or `own` = vitals you recorded and notes you wrote; an entry outside it answers 404 and is logged as `access.denied`. Writing a correction needs both: `clinical.notes.write` on the encounter and `clinical.records.void` on the original.
- **Checking in** is `POST /api/clinical/encounters/check-in` with `scheduling.appointments.check_in`: it changes the appointment and opens the encounter, so it lives in Clinical, which sits above Scheduling.
- **Staff can hold several roles.** A Head Nurse account typically has *Head Nurse* only, since that role already includes everything a staff nurse can do.

## Enforcement

### Backend

```csharp
// Endpoint declares what it needs
group.MapPost("/encounters/{id}/vitals", RecordVitals.Handle)
     .RequirePermission(Perm.Clinical.VitalsRecord);

// Handler checks the specific record against the user's scope
var encounter = await db.Encounters.FindAsync(id);
if (!await scope.CanAccessEncounter(user, Perm.Clinical.VitalsRecord, encounter))
    return Results.Forbid();

// List endpoints filter by scope in SQL, not after loading
var query = db.Encounters.WhereInScope(scope.For(user, Perm.Clinical.EncountersView));
```

1. **`RequirePermission`** registers an ASP.NET authorization policy. It checks that the user has the permission at any scope. Users without it get `403`.
2. **The scope check** runs in the handler (single record) or as a query filter (lists). A record outside scope returns **`404` instead of `403`**, so users can't probe which records exist.
3. **Permissions are cached per user**, keyed by `permissions_version`. Changing a role bumps the version for every user holding it, so the change takes effect on the user's next request.
4. **Stored grants are never trusted blindly.** When permissions load, any `role_permissions` row pointing at an unknown permission or a disallowed scope is logged and ignored.
5. **The catalog lives in code** (`Perm.cs` in the shared kernel) and mirrors the table above. Unit tests fail if the code catalog or the seeded default roles drift from this document, so they're always changed together.
6. **Reference lists are readable by any signed-in user.** Departments, positions and specializations (`GET /api/workforce/{list}`) feed forms and filters across the app, including the portal's "book by specialization", so they use `RequireSignedIn()`. Changing them needs `workforce.employees.manage`. Shift definitions (`GET /api/workforce/shifts`) are readable the same way for rosters and schedules; changing them needs `workforce.shifts.manage`.
7. **Scopes reach code as an `AccessScope`** (`ICurrentPermissions.ScopeForAsync(perm)`). Each module turns it into a filter for its own tables, e.g. `employees.WhereInScope(scope)`: `all` = no filter, `own` = the employee linked to the signed-in user. `IStaffScope` (Workforce, one load per request) resolves "my employee record" and "my wards today" in hospital time; ward assignments and roster entries use it for `ward` (records on my wards) and `own` (my records), combined as a union. Whole-ward views (the roster grid) need `all` or `ward`; `own` alone gets "My schedule". A single record outside scope returns `404` and is logged as `access.denied` with reason `out_of_scope`.

### Guardrails

- *System Administrator* is `is_system`. It holds every permission that can be granted at `all` (computed from the catalog, so new permissions are included automatically) and can't be deleted or edited. The two `own`-only permissions (requesting a shift swap, requesting a patient link) are personal actions and aren't included.
- **Roles still held by users can't be deleted.**
- **The last active System Administrator can't be removed** or disabled.
- **Users can't change their own roles**, and can't disable their own account.
- **Changing who is a System Administrator needs `access.roles.manage`**: granting or removing the role, creating a user with it, sending a staff invite that grants it, and disabling or enabling an administrator's account. Without this, anyone with `access.users.manage` (e.g. HR) could promote themselves through a colleague. It's still a permission check, not a role-name check.
- **Every role and permission change is audited** (`access.role.permissions_changed` with a before/after diff). So are denied requests (`access.denied`). See [AUDIT.md](AUDIT.md).
- **Portal users never get staff permissions**, even by mistake. Accounts are either `staff` or `portal` (`users.account_type`). Self-registered portal accounts get the portal role (`roles.is_portal`), and the API keeps the two apart: a portal account can hold only the portal role, and the portal role can't be given to a staff account, whether by editing roles, creating a user or sending a staff invite. Staff who also need the portal register a separate portal account. The portal role can't be deleted, and its editor explains that it should keep to `linked` and `own` scopes.

### Frontend

- `GET /api/access/me` returns the user and `permissions: { "clinical.vitals.record": ["ward", "own"], … }`: every scope the user holds per permission, widest first. Employee and linked-patient details are added by later slices.
- `*appHasPermission`, `permissionGuard`, and the sidebar all read from that map.
- The frontend never decides scope. It just shows what the API returns.

### Tests

- **Matrix test:** for every row of the default-role table, call the endpoint as a seeded user of each role and assert `2xx`, `403` or `404` as expected.
- **Scope tests:**
  - A Ward 3 nurse can't read a Ward 5 patient.
  - After a transfer to Ward 5, the Ward 3 nurse loses access.
  - A representative without `can_view_records` can book but can't read records.
- **Coverage test:** every `/api` endpoint must declare exactly one access rule: `.RequirePermission(Perm.X)`, `.RequireSignedIn()` (any signed-in user, e.g. `/me`), or `.AllowAnonymous()` (e.g. login). **The build fails otherwise.**

# Build Plan

Status: **Accepted (2026-11-09).**

## How the plan is organized

**Phases made of feature slices.**
- **Phases** follow dependency order. You can't book an appointment without doctors, or record vitals without encounters. Each phase ends in a demoable milestone and a release tag.
- **Slices** are the unit of work. Each one is a thin, finished feature through every layer (database → API → UI → tests), done as one branch and one PR.

So phases answer "what next?" and slices answer "what am I building today?" Every slice follows the definition of done in [CONVENTIONS.md §7](CONVENTIONS.md#7-definition-of-done-every-slice).

**Scope rules (so the scope can't quietly grow)**
- A slice builds only what its acceptance criteria say. Ideas go into the backlog at the bottom.
- The scope service grows with the modules. `ward`, `own` and `linked` are each built in the slice that first needs them, not upfront.
- Every phase ends with its demo script run end to end and a release tag. From Phase 1 on, each milestone is an automated Playwright test in `web/e2e/milestones/` that drives the UI as each role, so the demo can be rerun any time.

**Sizes:** S ≈ 1–2 days · M ≈ 3–5 days · L ≈ 1–2 weeks of focused solo work. These are for ordering and spotting oversized slices, not deadlines.

---

## Phase 0: Foundation → `v0.1.0`

**Milestone:** an admin logs in, creates a role, ticks permissions, assigns it to a user, and that user's sidebar changes. Every step is visible in the audit log, in light and dark mode.

| # | Slice | Size | Acceptance criteria |
|---|---|---|---|
| 0.1 | **Repo and tooling** | S | `git init`, layout from CONVENTIONS §2, `global.json`, `.nvmrc`, `Directory.Build.props`, central packages, `.editorconfig`, `docker-compose.yml` (Postgres 18 + Mailpit), README skeleton, public GitHub repo, `main` protected |
| 0.2 | **Backend skeleton** | M | Api host, SharedKernel, Infrastructure. `AppDbContext` with schemas per module, snake_case, UUID v7, `timestamptz`, `xmin`, `citext` + `btree_gist` migration. Serilog, ProblemDetails, health check, OpenAPI. Test projects: Testcontainers + Respawn harness, NetArchTest rules. |
| 0.3 | **Frontend skeleton** | M | Angular 22 workspace with pnpm, Tailwind v4, token file (light/dark), Inter, Spartan CLI set up, Lucide icons, ESLint/Prettier, Vitest, Playwright + axe, `/api` dev proxy, ng-openapi-gen pipeline |
| 0.4 | **CI** | S | Workflow from CONVENTIONS §6 (API, web, contract jobs) green on an empty app. CI badge in README. |
| 0.5 | **Design system core** | L | Spartan components adapted to tokens: button, input, textarea, label, select, checkbox, switch, dialog, alert dialog, sheet, dropdown, tooltip, tabs, card, toast, skeleton, alert. Ours: `form-field`, `status-pill` + status map, `page-header`, `empty-state`, `data-table` (TanStack, server-side paging). `ThemeService` + toggle. `/design` route showing all of it. |
| 0.6 | **Login and app shell** | M | Identity with lockout, cookie session, antiforgery. `POST /api/access/login`, logout, `GET /api/me`. Login page, auth guard, app shell (sidebar, top bar, user menu, theme toggle). Seeded System Administrator. |
| 0.7 | **RBAC engine** | L | Permission catalog (all permissions from RBAC.md, so seeded roles are complete), `roles` / `role_permissions` / `user_roles`, `RequirePermission`, per-user permission cache keyed by `permissions_version`, `all` scope, endpoint-coverage test, guardrails (system role, last admin, no self-role-edit). Seed the 8 default roles. |
| 0.8 | **User and role admin UI** | M | Users list/create/disable/enable/unlock, assign roles. Roles list, role editor with `permission-matrix`. `*hasPermission`, `permissionGuard`, sidebar from permissions. |
| 0.9 | **Audit core** | M | `audit_log` table, append-only by trigger (restricted role in Phase 4), event catalog, `.Audited(...)` + coverage test, data-change interceptor, correlation IDs, auth events, `access.denied`. Basic audit log page (filters: date, user, event, severity). |

## Phase 1: Workforce + Facility → `v0.2.0`

**Milestone:** HR creates a nurse and sends an invite. The nurse sets a password. The Chief Nurse assigns them to Ward 3 and puts them on the roster. The nurse sees their own schedule, and a head nurse sees only their own ward's roster.

| # | Slice | Size | Acceptance criteria |
|---|---|---|---|
| 1.1 | **Lookups** | S | Departments, positions, specializations: one reusable list/edit pattern, `is_active` instead of delete |
| 1.2 | **Employees** | M | Create/edit/list (filters: department, type, status), profile page, physician profile section, license check. `own` scope for `workforce.employees.view`. |
| 1.3 | **Staff invites** | M | Send/revoke invite (email via Mailpit), accept page sets password, links the user to the employee and grants the invite's roles. Expired and used tokens rejected. |
| 1.4 | **Wards and beds** | S | Ward CRUD, bed management within a ward. Capacity shown as active bed count. |
| 1.5 | **Shifts** | S | Shift definitions (code, times, crosses midnight, color token), `shift-chip` |
| 1.6 | **Ward assignments + `ward` scope** | M | Assign/end assignment (exclusion constraint → friendly 409). Scope service resolves "my wards today." Integration tests prove ward scoping on assignments. |
| 1.7 | **Duty roster** | L | Ward roster grid (week view) with add/edit/remove entries, one-shift-per-day rule. "My schedule" page. Scoped by `ward` / `own`. |
| 1.8 | **Shift swaps** | M | Request → target accepts/declines → head nurse approves/rejects. On approval, both entries swap in one transaction. Inbox views for each party. *If phase 1 runs long, move this to phase 5.* |

## Phase 2: Patients + Scheduling → `v0.3.0`

**Milestone:** a daughter registers on the portal and requests a link to her father. Front desk verifies it. She books a slot with Dr. Santos, who confirms. Front desk sees it on the day list. Every view of the father's record shows up in the audit log.

| # | Slice | Size | Acceptance criteria |
|---|---|---|---|
| 2.1 | **Patient registry** | M | Register (MRN generated), search by name/MRN/birth date, demographics edit, `patient-header`. Record-view logging (15-minute dedupe). |
| 2.2 | **Allergies** | S | Add/update/resolve/entered-in-error. Severe allergies show the banner in `patient-header`. |
| 2.3 | **Portal shell + self-registration** | M | Separate mobile-first portal routes (16px base, 44px targets). Register, log in, empty "no linked patients yet" state. |
| 2.4 | **Patient account links + `linked` scope** | M | Portal: request link. Front desk: pending-links queue, verify/reject (with relationship + `can_book` / `can_view_records`), revoke. One-self-link constraint. Scope tests: links grant and revoke access immediately. |
| 2.5 | **Availability slots** | M | Physician (or front desk) publishes slots for a date range from a simple weekly pattern form, and closes slots. Overlap rejected by the database. `own` scope for physicians. |
| 2.6 | **Booking** | M | Browse open slots by specialization/doctor/date. Staff book for any patient, portal users for linked patients with `can_book`. One live appointment per slot. |
| 2.7 | **Appointment workflow** | M | Doctor inbox: confirm/decline. Cancel (all parties per scope), no-show. Front desk day list. Portal "my appointments" per linked patient. |

## Phase 3: Clinical → `v0.4.0`

**Milestone:** the full main flow from ARCHITECTURE.md §4, steps 5–10, runs as an automated Playwright test across five roles.

| # | Slice | Size | Acceptance criteria |
|---|---|---|---|
| 3.1 | **Encounters + physician `own` scope** | M | Check-in opens an outpatient encounter. Encounter page (patient header, tabs: summary, vitals, notes, orders, MAR). Physician `own` = encounters/appointments they're attached to. |
| 3.2 | **Vital signs** | M | Record vitals, range flags (`vital-value`), shared normal/critical config, small trend chart (charting library chosen here). |
| 3.3 | **Clinical notes + voiding** | M | Note types, 24-hour edit window, then void-and-replace. Shared void flow (`confirm-action` with required reason), used by vitals and MAR as well. |
| 3.4 | **Admission** | M | Admit from an encounter or directly. Bed picker shows only free beds. Inpatient encounter + admission + first bed assignment in one transaction. One admission per patient enforced. |
| 3.5 | **Ward census board** | M | `bed-tile` grid per ward from `current_census`, flags (allergy, critical vitals), ward-scoped. Nurse home screen. |
| 3.6 | **Transfer + discharge** | M | Transfer = close and open bed assignments, with reason. Scope follows the patient (integration test). Discharge with disposition + discharge summary note. |
| 3.7 | **Physician orders** | M | Create medication/lab/diet/nursing orders, discontinue with reason. Medication fields enforced. |
| 3.8 | **Medication administration (MAR)** | M | Per active medication order: record given/held/refused/not available, reason required when not given, allergy warning shown before recording |
| 3.9 | **Portal records view** | S | Linked users with `can_view_records` see visit/discharge summaries, vitals, appointments, nothing else |
| 3.10 | **Main-flow E2E** | M | Playwright scripts the whole flow across roles, with axe checks on every page visited |

## Phase 4: Polish + ship → `v1.0.0`

**Milestone:** a stranger opens the live demo, picks a role from the login page, and can try every part of the system safely.

| # | Slice | Size | Acceptance criteria |
|---|---|---|---|
| 4.1 | **Role dashboards** | M | Home per role: nurse (my ward census, my shifts, swap inbox), physician (today's appointments, my inpatients), front desk (day list, pending links), chief nurse (ward occupancy, staffing), admin (critical audit events in 24h, users) |
| 4.2 | **Audit completion** | M | Patient "Access history" tab, audit viewer with related entries and CSV export (audited), hash-chain sealing job + verify endpoint |
| 4.3 | **Demo data + accounts** | M | Seeder generating realistic fake hospital data (wards, staff, patients, history). One demo login per role on the login page. Nightly reset. |
| 4.4 | **Quality pass** | M | axe clean on all pages, keyboard walkthrough, empty/error states reviewed, slow-query check with seeded volume, security headers |
| 4.5 | **Hosting + deploy** | M | Pick host (CONVENTIONS §8). Production Dockerfile, deploy workflow on tag, HTTPS, backups. |
| 4.6 | **Showcase docs** | S | README with screenshots/GIF, architecture diagram, "try it" guide per role, links to ADRs. Write-up of the three-to-one consolidation story. |

## Phase 5: Deferred (pick as wanted)

| Feature | Notes |
|---|---|
| Shift swaps | Only if moved out of phase 1 |
| Incident reports | From mantrix. New module or part of Workforce. |
| Performance evaluations | From mantrix |
| Roster pattern generator | Fill a month's roster from a weekly pattern |
| Notifications | In-app + email for appointment decisions, swap requests, link verification |

## Backlog (ideas that came up during the build)

| Idea | Why | Raised in |
|---|---|---|
| "Must change password on first sign-in" for users created with a temporary password | Today the temporary password stays until changed by an administrator | 0.8 |
| Restricted database role for the app (`INSERT`/`SELECT` only on `audit_log`) | 0.9 enforces append-only with a trigger, which also covers the owner. A separate role is a deployment concern. | 0.9 |
| Disable the sign-in account when an employee is separated, or prompt to | Separating an employee (1.2) doesn't touch their user account, so a departed staff member can still sign in until an administrator disables them | 1.2 |
| Keep toasts clear of action buttons | Bottom-right toasts can sit over a card's buttons right after saving, and they pause while hovered (found by the Phase 1 milestone test) | 1.8 |
| Warn about a possible duplicate when registering a patient with the same name and date of birth | The registry page asks staff to search first, but nothing stops a second record for the same person | 2.1 |
| Confirm the email address of new portal accounts | Self-registration (2.3) signs in straight away. An unconfirmed email is harmless until a link is verified, but it blocks password reset by email later. | 2.3 |
| Close the clinic visit when the patient is admitted from it, or prompt the physician to | Admitting from a visit (3.4) links the two but leaves the visit open, so the physician closes it separately | 3.4 |
| Undo a transfer or discharge entered in error | 3.6 records both for good; a mistaken move is fixed by moving the patient back (two rows in the bed history), and a mistaken discharge can't be reopened | 3.6 |
| Completing an order, and correcting one written in error | 3.7 can create and discontinue only. `completed` exists in the status check but nothing sets it; a wrong order is discontinued with the reason, not voided or edited. 3.8 left it: one dose doesn't finish a recurring order, so completing needs a rule (a dose count or end date on the order, or a "complete" action). | 3.7, 3.8 |
| Allergy warning when writing a medication order | 3.8 warns the nurse before a dose; the physician sees only the severe-allergy banner when ordering. `ui-allergy-warning` and `AllergyCheck` could be reused in the order dialog. | 3.8 |
| Allergy match by drug class | `AllergyCheck` matches names only, so penicillin vs amoxicillin isn't caught; the warning lists every allergy and asks for a confirmation to cover that | 3.8 |
| Refresh the ward board on its own (polling or server push) | The board (3.5) refreshes on load and with a Refresh button, so a nurse who leaves it open sees new admissions and critical readings only after refreshing | 3.5 |
| One web helper for "active and severe" allergies | The API has one rule (`PatientAllergy.IsActiveAndSevere`, 3.5), but the patient record and encounter pages still filter the severe ones themselves for the header banner | 3.5 |
| Enforce portal role scopes in the role editor | The portal role's editor explains it should keep to `linked` and `own` scopes, but saving `all` or `ward` isn't blocked yet | 2.3 |
| Let portal users withdraw a pending link request | A mistaken request waits for the front desk to reject it; requesters can't cancel it themselves | 2.4 |
| Void an encounter opened by mistake (e.g. the wrong patient checked in), and undo the appointment's check-in | 3.1 only opens and closes encounters; `voided` is in the status list but nothing sets it yet | 3.1 |
| Decide who records vitals on clinic visits | Staff Nurse and Head Nurse hold `clinical.vitals.record` at `ward`, which only reaches admitted patients, so by default only physicians (their own patients), the Chief Nurse and admins can record vitals on outpatient visits. Main-flow step 5 has "a nurse" do it; 3.10 needs an answer (e.g. an OPD nurse role at `all`). | 3.2 |

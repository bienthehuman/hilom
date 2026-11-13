# How three apps became one

Hilom started as three separate Laravel apps. Each one solved one part of a hospital's work, and each one was fine alone. Put side by side, they described the same hospital three times, in three different ways. This is the story of turning them into one system, and of the decisions that took.

## Where it started

Between February and April 2026 I built three web apps for a client who needed to streamline their processes: let people know what needs tending to, manage their wards, and run the rest of their work more efficiently. The client asked to stay anonymous. That was the condition under which they let me keep ownership of the system, so they aren't named here.

| App | Built | What it did | Stack |
|---|---|---|---|
| [hospital-care-system](https://github.com/bienthehuman/hospital-care-system) | Feb–Mar 2026 | Doctors publish weekly schedules, patients request a slot, doctors accept or decline. Admins invite doctors with one-time, expiring links. | Laravel 10, Bootstrap 5, jQuery and AJAX |
| [wardlink](https://github.com/bienthehuman/wardlink) | Mar–Apr 2026 | Ward admissions and transfers, vital signs, charting, physician orders, medication administration, allergies. A patient can read their own record. | Laravel, Blade |
| [mantrix](https://github.com/bienthehuman/mantrix) | Mar–Apr 2026 | Nurse shifts, ward (station) assignments, the duty roster and shift swaps, plus inventory, incident reports, performance evaluations and disease logs. | Laravel |

Each app did its job. The trouble showed when I looked at them together:

- **The same things existed three times.** Users, doctors, wards and vital signs each had their own tables. There were two `vitals` tables with different shapes. A doctor in hospital-care-system and a doctor in wardlink had nothing to do with each other.
- **Every app hardcoded its roles.** `role:admin` middleware in one, `enum('chief_nurse','head_nurse','nurse')` in another, separate login pages per role in the third. Adding a role meant changing code in many places.
- **Rules were checked in PHP, just before the insert.** hospital-care-system checked for overlapping schedules that way. Two requests at the same moment could both pass, which means a double-booked slot or two patients in one bed.
- **People were stored as text.** Fields like `recorded_by`, `physician_name` and `administered_by` held typed names, so "who gave this dose?" was a string, not a person.
- **Clinical data could disappear.** `cascadeOnDelete` on clinical tables meant deleting a ward or a doctor could take patient history with it.
- **Three looks.** Bootstrap, jQuery and some Tailwind, with no shared design.

When the project became mine, the job was to streamline it further. Fixing each app on its own would have kept every one of those problems three times over, so I designed one system and rebuilt it from scratch.

## Deciding before building

I wrote the design before any code: architecture, data model, access control, audit logging, design system, conventions and a build plan. All of it was accepted on 9 November 2026 and lives in [`docs/`](README.md). Most of that time went into answering one question for every feature: keep it, merge it, defer it or cut it.

**Kept and merged.** Doctor schedules and appointment requests from hospital-care-system. Admissions, transfers, vitals, notes, orders, the medication record and allergies from wardlink. Shifts, ward assignments, the roster and shift swaps from mantrix. hospital-care-system's doctor invites became staff invites for everyone, and mantrix's nurse-only staff list became employees of every kind.

**Deferred.** Incident reports, performance evaluations and the monthly roster generator from mantrix, and notifications. They're useful, but they don't connect the other modules, so they wait for [phase 5](BUILD_PLAN.md#phase-5-deferred-pick-as-wanted).

**Cut.** mantrix's inventory, which is a separate domain that would have roughly doubled the scope. Its disease analytics, which can come from encounter diagnoses later if anyone wants them. And wardlink's separate login page per role: there is one sign-in, and what you see depends on your permissions.

To check that the merged design held together, I wrote one storyline that runs through every module ([Architecture §4](ARCHITECTURE.md#4-how-the-modules-connect-the-main-flow)). HR hires Dr. Santos. A patient's daughter registers in the portal and the front desk verifies her link to her father. She books him an appointment, he's checked in, and then admitted to Ward 3. Nurse Reyes, assigned to Ward 3, records his vitals and gives his medications. He's transferred, and discharged, and his daughter reads the summary. That story touches all three old apps, and it became both the demo script and the end-to-end test for each phase.

## The decisions that made it one system

Each of these has a short decision record. They are the places where merging forced a better answer than any of the three apps had.

**Something to hang clinical data on** ([ADR 0004](adr/0004-encounter-as-clinical-spine.md)). hospital-care-system attached vitals and assessments to a *user* by date. wardlink attached everything to an *admission*. Neither covers both a clinic visit and a ward stay. Hilom has **encounters**: an outpatient visit or an inpatient stay, and every vital sign, note, order and dose points at one. A stay can point to the visit it came from. One vitals table, one set of screens, and a patient's history is just their encounters in order.

**Roles as data, permissions in code, and scopes** ([ADR 0002](adr/0002-rbac-permissions-in-code-roles-as-data.md)). Instead of three hardcoded role lists, there's a catalog of permissions in code, and administrators build roles from them in the UI. Each grant carries a scope: *all*, *ward*, *own* or *linked*. Scope is worked out from live data, so when a patient is transferred, the old ward's nurses lose access and the new ward's gain it on the next request, without anyone touching a setting.

**Patient accounts that fit real families** ([ADR 0003](adr/0003-patient-account-links.md)). wardlink's `patients.user_id` allowed one account per patient and nothing else. Many patients don't want an account, and many have a daughter or son who handles things for them. In Hilom an account is optional, links are many-to-many, and front desk staff verify every link before it grants anything.

**Let the database say no** ([ADR 0006](adr/0006-database-enforced-invariants.md)). The rules that must never break are PostgreSQL constraints: exclusion constraints for slots, bed occupancy and ward assignments, partial unique indexes for one live appointment per slot and one shift a day, and check constraints for vital ranges and medication orders. App code still validates first, for a friendly message. The database is the guard.

**Know who did what** ([ADR 0007](adr/0007-audit-critical-events.md)). None of the old apps could answer "who moved this patient, and who looked at their chart?" Hilom logs named events, field-level changes, patient-record views, failed sign-ins and refused requests, in the same transaction as the change. The log is append-only through database grants and sealed into a hash chain. Since the quality pass, the API connects as a database role that can't update or delete it.

**One look** ([ADR 0008](adr/0008-design-system-spartan.md)). One design system on Spartan UI and Tailwind tokens, in light and dark mode, checked with axe on every page the end-to-end tests visit.

## How it was built

The stack is new: ASP.NET Core on .NET 10, EF Core and PostgreSQL 18 behind an Angular 22 front end. Ideas came across from the old apps; no code or data did.

The backend is a modular monolith ([ADR 0001](adr/0001-modular-monolith.md)): one deployable app, six modules, one Postgres schema per module, and architecture tests that keep the dependencies pointing one way. Three separate apps had shown me what drifting apart looks like, and microservices for one hospital and one developer would have brought the same drift back with network calls added.

The [build plan](BUILD_PLAN.md) splits the work into phases in dependency order (foundation, workforce, patients and scheduling, clinical, polish) and each phase into slices. Each slice is one pull request with its own acceptance criteria, tests and doc updates. Each phase ends with its demo script running as an automated Playwright test, driving the UI as each role, and a release tag. The first commit was on 9 November 2026, and by 13 November 43 pull requests had been merged and phases 0 to 3 released.

Phase 4 added the things that make it showable: a dashboard per role, a generated demo hospital with a login for every role and a nightly reset ([ADR 0009](adr/0009-demo-data-through-the-domain.md)), and a quality pass for accessibility, slow queries and security headers. The demo data is created through the same domain rules as real data, so a generated history can't contain a patient in two beds or a dose without an order.

## What it fixed along the way

Beyond merging, the rebuild fixed things that were quietly wrong in the originals:

- Staff are foreign keys to employees, not names typed into a text field.
- A patient's age is calculated from the date of birth, not stored and left to go stale.
- A medication dose links to the order it fulfils, and warns about a matching allergy before it's recorded.
- Blood pressure is two numbers, not a string.
- Clinical records are never deleted. A record entered in error is voided, with a reason, and stays visible as voided.
- Deleting a ward or a doctor can never erase patient history.

## What's next

Phase 4 is finished apart from hosting, and where Hilom will be hosted is still an open question. Until that's settled it runs locally, with the demo hospital, as the [README](../README.md#run-it-locally-with-the-demo-hospital) describes. The features deferred from mantrix (incident reports, performance evaluations, the roster pattern generator) and notifications are listed in [phase 5](BUILD_PLAN.md#phase-5-deferred-pick-as-wanted), and the [backlog](BUILD_PLAN.md#backlog-ideas-that-came-up-during-the-build) holds the smaller ideas that came up during the build.

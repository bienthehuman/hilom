# Hilom — Design Docs

*Hilom at Aruga: healing and care, in one hospital system.*

Status: **Design accepted (2026-11-09); phases 0–3 built and released, phase 4 in progress.** These docs describe what Hilom does and why. [How three apps became one](CONSOLIDATION.md) tells the story behind them.

## What this is

One hospital system that brings together three earlier projects, rebuilt on a new stack:

| Earlier project | What we take from it |
|---|---|
| hospital-care-system | Doctor schedules, appointment requests, specializations, staff invites, outpatient assessment |
| wardlink | Patients, admissions, ward transfers, vitals, charting, physician orders, medication administration, allergies |
| mantrix | Shifts, nurse ward assignments, duty roster, shift swaps |

Stack: ASP.NET Core (.NET 10) · EF Core · PostgreSQL · Angular · TypeScript · Tailwind CSS v4.

## Reading order

1. [ARCHITECTURE.md](ARCHITECTURE.md): scope, modules, how the pieces connect, tech choices, build phases
2. [DATA_MODEL.md](DATA_MODEL.md): every table, its relations and its database-level rules
3. [RBAC.md](RBAC.md): permissions, scopes, default roles, and how access is enforced
4. [AUDIT.md](AUDIT.md): what gets logged, the event catalog, and who can see the log
5. [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md): components, color tokens, typography, status colors, accessibility
6. [CONVENTIONS.md](CONVENTIONS.md): versions, repo layout, API/frontend/git conventions, CI, definition of done
7. [BUILD_PLAN.md](BUILD_PLAN.md): phases and slices, with acceptance criteria
8. [adr/](adr/): short decision records explaining the main choices
9. [CONSOLIDATION.md](CONSOLIDATION.md): how the three earlier apps became one, and why

## Decisions (accepted 2026-11-09)

1. **Ward scope for nurses:** based on ward assignment, not on being on shift right now.
2. **Bed-level tracking:** yes. The database blocks double-occupancy.
3. **Patient self-registration:** anyone can register, but sees nothing until front desk verifies a patient link.
4. **Roster patterns:** deferred to phase 5. Manual roster entry comes first.
5. **Hospital time zone:** Asia/Manila, configurable.
6. **Floating nurses:** no. One ward at a time.
7. **Appointment waitlist:** no. One live request holds the slot.
8. **Doctor patient access:** own patients (appointment or encounter with them). Admins can widen it.
9. **What portal users see:** visit/discharge summaries, vitals, appointments. No nursing notes or full medication record.
10. **Audit logging:** critical events, data changes, patient-record views, denied access and failed logins. Append-only, with tamper evidence. See [AUDIT.md](AUDIT.md).
11. **Design system:** Spartan UI, Clinical Teal on slate, Inter, light and dark mode. See [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md).
12. **Stack versions:** .NET 10, PostgreSQL 18, Angular 22, Node 24, pnpm. See [CONVENTIONS.md](CONVENTIONS.md).
13. **Build plan:** phases made of feature slices, one PR per slice. See [BUILD_PLAN.md](BUILD_PLAN.md).
14. **Name:** Hilom (Tagalog for "healing"), tagline *Hilom at Aruga* ("healing and care"). Code namespaces `Hilom.*`, repo `bienthehuman/hilom`.

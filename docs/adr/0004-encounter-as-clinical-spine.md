# ADR 0004: Encounters tie clinical data together

**Status:** Accepted · **Date:** 2026-11-09

## Context
hospital-care-system attached vitals and assessments to a *user* by date. wardlink attached vitals, charts, orders and MAR to an *admission record*. Merging them needs one thing to hang clinical data on, covering both clinic visits and ward stays.

## Decision
- `encounters` represents one episode of care: `outpatient` (from an appointment check-in) or `inpatient` (with an `admissions` row).
- Vitals, notes, orders and medication administrations all reference `encounter_id`.
- An inpatient encounter can point to the outpatient visit it came from (`parent_encounter_id`).
- Ward location is tracked by `bed_assignments` with time ranges. A transfer is a new row, not an update.

## Consequences
- One vitals table, one notes table, one set of screens for both settings.
- A patient's timeline is simply their encounters in order.
- "Who can see this?" always resolves through the encounter (attending physician → `own`, current bed's ward → `ward`, patient's links → `linked`).

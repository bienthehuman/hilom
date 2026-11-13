# ADR 0009: Generate demo data through the domain, and reset by rebuilding

**Status:** Accepted · **Date:** 2026-11-13

## Context
The showcase needs a believable hospital to click through: months of appointments, stays, vitals and medication rounds, and an audit trail to match. It also needs logins for each role, and it must look fresh every day it's shown. Raw inserts would be quick, but they'd skip the rules the product enforces (a bed holds one patient, a dose follows its order, nothing is recorded after discharge), so the data could show states the product can never produce. They'd also leave an audit log that says nothing.

## Decision
- **A separate project, `Hilom.DemoData`,** sits above every module, like the host. No module or the infrastructure refers to it (an architecture test checks). It's switched on by `Demo:Enabled`; with it off, nothing is registered except an endpoint that answers "not enabled".
- **Everything is made through the domain types** (`Admission.Admit`, `Appointment.Confirm`, `PhysicianOrder`, `Encounter.Close`, ...). If a rule says no, generation stops with the rule's message, because that's a bug in the generator.
- **Each step is saved as the person who'd do it, at the time it happened.** The run has its own database context whose interceptors use a simulated clock and the acting user, so the real timestamp, audit-event and field-change interceptors write the history. Events and reads use `HistoricalAuditWriter`, which writes entries the way `IAuditLog` does.
- **The run is deterministic.** All randomness comes from `Demo:Seed`. Entity ids come through `EntityIds` (a hook the base `Entity` uses; UUID v7 unless a run overrides it), drawn from the same generator. The same seed, scale and end moment give the same ids, names, numbers and times. Password hashes differ (salted), and so do the first administrator's ids and the real-clock timestamps of startup seeding.
- **The end of history is "this hour"** unless `Demo:Now` is set, so the hospital always looks current. Tests pin it.
- **Scale is a setting.** `Standard` is sized for the Phase 4 slow-query check; `Small` is quick on a laptop; `Tiny` is for tests.
- **One login per role** (the demo accounts, `*@demo.hilom.test`) shares one published password. They're listed by an anonymous endpoint, only in demo mode. Every other generated account gets a password nobody knows.
- **Nightly reset drops and rebuilds.** A background job drops the module schemas and the migration history, migrates, and generates again. Dropping is what the append-only audit trigger permits; it refuses row changes, not removal of the schema. While it runs, `/api` answers 503 `demo_resetting` with `Retry-After`.
- **A marker table guards destructive steps.** Generation only runs on a database with no patients or staff, and writes `public.demo_dataset`. The reset refuses a database without the marker, so pointing demo mode at a real database does nothing to it.

## Alternatives considered
- **SQL or CSV fixtures:** fast, but they bypass the rules and the audit trail and go stale as the model changes.
- **Calling the HTTP API as each user:** the most faithful, but slow (password hashing per sign-in, one request per record) and the clock can't be set, so history couldn't be backdated.
- **Truncating tables for the reset:** the audit log refuses `TRUNCATE`. Weakening that trigger for the demo would weaken it everywhere.

## Consequences
- A change to a domain rule can break generation. That's the point: it fails in the seeder tests before the data lies.
- Generation takes minutes at `Standard`, so a reset takes the demo offline for that long (at 03:00 hospital time by default).
- The demo password is public by design. Demo mode must never be switched on for a deployment with real data; the marker and empty-database checks stop most mistakes, not all.

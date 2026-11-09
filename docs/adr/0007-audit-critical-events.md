# ADR 0007: Audit critical events, data changes and patient-record views

**Status:** Accepted · **Date:** 2026-11-09

## Context
The first draft only logged field-level data changes. That misses much of what matters in a hospital system: logins and failed logins, requests that were denied, who *opened* a patient's record, and business events such as a transfer or a permission change. Field diffs alone can't answer "who moved this patient, and why?"

## Decision
- **One `access.audit_log` table with three kinds of entries:** named events, field-level data changes, and sensitive reads.
- **Named events come from a code catalog**, like permissions. Each event has a fixed severity (`info` / `notice` / `critical`).
- **Each state-changing endpoint declares its event**, or opts out with a reason. A build-time test enforces it.
- **Events are written in the same transaction as the change.**
- **Denied requests and failed logins are logged.**
- **Patient-record views are logged**, deduplicated per user and patient every 15 minutes.
- **The log is append-only** through database grants. A background job seals rows into a hash chain for tamper evidence.

## Alternatives considered
- **Data-change interceptor only:** no reads, no auth events, no intent ("transfer" shows up as just a few changed columns).
- **A separate log store (e.g. Seq, Elasticsearch):** good for operational logs, but audit entries must commit atomically with the change they describe, so they belong in Postgres. Serilog still handles operational logging.
- **Database triggers:** they can't see the app user, the request, or the reason.

## Consequences
- Every new endpoint needs one more line (`.Audited(...)`).
- Admins and the Chief Nurse can answer "who looked at or changed this patient's record?"
- Read logging adds one insert per chart open at most every 15 minutes per user. That's negligible at this scale.

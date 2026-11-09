# ADR 0006: Enforce scheduling and occupancy rules in PostgreSQL

**Status:** Accepted · **Date:** 2026-11-09

## Context
The old apps checked overlaps in PHP before inserting. Two simultaneous requests can both pass that check, giving a double-booked slot or two patients in one bed.

## Decision
Use PostgreSQL constraints for rules that must never break:
- `EXCLUDE USING gist` with `btree_gist` and range types: physician slots, bed occupancy, one bed per admission, one admission per patient, one ward per employee.
- Partial unique indexes: one live appointment per slot, one shift per day, one active self-link per patient.
- Check constraints: vitals ranges, medication order fields.

App code still validates first, for friendly messages. The database is the final guard, and its errors are mapped to `409 Conflict` ProblemDetails.

## Consequences
- Requires the `btree_gist` extension and raw SQL in some EF migrations.
- Integration tests must run against real PostgreSQL (Testcontainers), not an in-memory provider.

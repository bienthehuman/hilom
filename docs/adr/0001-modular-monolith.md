# ADR 0001: Modular monolith, one DbContext, one schema per module

**Status:** Accepted · **Date:** 2026-11-09

## Context
The three earlier apps were separate Laravel monoliths with duplicated users, doctors, wards and vitals. The new system must feel like one product. It's built and run by one developer.

## Decision
- One ASP.NET Core app, split into six modules (Access, Workforce, Facility, Patients, Scheduling, Clinical), each its own project.
- One `AppDbContext`. Each module keeps its tables in its own Postgres schema and contributes its own EF configurations.
- Only the owning module writes its tables. Foreign keys across schemas are allowed.
- Module dependency direction is enforced with architecture tests.

## Alternatives considered
- **Microservices:** separate deploys, network calls and duplicated data for a single developer and a single hospital. Rejected.
- **A DbContext per module:** stricter isolation, but foreign keys across contexts and separate migration histories add friction with little benefit here. Can be split later if a module ever needs to stand alone.
- **One flat project:** fast to start, but the boundaries between the old apps would blur again.

## Consequences
- Simple to run (`docker compose up`), debug and deploy.
- Boundaries are kept by convention plus tests, not by the compiler alone.

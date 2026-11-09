# ADR 0002: Permissions in code, roles as data, scoped grants

**Status:** Accepted · **Date:** 2026-11-09

## Context
All three earlier apps hardcoded roles (`role:admin`, `enum('chief_nurse','head_nurse','nurse')`). Adding a role meant code changes in many places. The new system should let admins shape roles without a deploy, and restrict *which records* a role reaches, not only which screens.

## Decision
- **Permissions** (`clinical.vitals.record`) are a static catalog in code. Code checks permissions only, never role names.
- **Roles** are database rows. Admins pick permissions for each role in a UI.
- **Each grant carries a scope** (`all` / `ward` / `own` / `linked`). The catalog lists which scopes each permission supports.
- Scope is evaluated against live data (ward assignments, bed assignments, attending physician, account links), so access follows real-world changes automatically.

## Alternatives considered
- **Hardcoded roles:** what we had. Inflexible.
- **Permissions as data too:** admins could create permissions that no code enforces. That's misleading, so rejected.
- **Full ABAC / policy engine (OPA, Casbin):** more power than needed, and harder to explain and test. Four scopes cover every case in scope.

## Consequences
- New features add catalog entries. Seeded roles get them through a migration.
- Every endpoint must declare a permission, enforced by a test.
- Scope logic is central code that must be well tested, since it's the most security-sensitive part of the app.

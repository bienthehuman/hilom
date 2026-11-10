# Hilom

[![CI](https://github.com/bienthehuman/hilom/actions/workflows/ci.yml/badge.svg)](https://github.com/bienthehuman/hilom/actions/workflows/ci.yml)

*Hilom at Aruga: healing and care, in one hospital system.*

Hilom is a hospital information system that handles outpatient appointments, inpatient ward care and hospital staff in one place. It combines three earlier projects I built for a nursing-students client into a single, consistent system, rebuilt from scratch on a new stack.

> **Status:** Phase 1 (workforce and facility) released as `v0.2.0`. Phase 2 (patients and scheduling) is complete and its milestone test passes; Phase 3 (clinical) is next. See the [build plan](docs/BUILD_PLAN.md).

## What it does

- **Scheduling:** doctors publish availability. Patients, their representatives or front desk book appointments.
- **Clinical:** check-in, admission to a ward bed, transfers, vital signs, clinical notes, physician orders, medication administration, discharge.
- **Workforce:** staff records and onboarding, ward assignments, duty roster, shift swaps.
- **Patient portal:** optional accounts. One account can manage several patients (e.g. a daughter for her parents), and staff verify every link.
- **Access control:** admins build roles from permissions in the UI, and each permission is scoped to *all*, *my ward*, *my own* or *linked* records. Nothing is hardcoded.
- **Audit:** critical actions, data changes, patient-record views, failed logins and denied access are all logged in an append-only, tamper-evident log.

## Stack

| Layer | Tech |
|---|---|
| Backend | ASP.NET Core (.NET 10), EF Core, PostgreSQL 18 |
| Frontend | Angular 22, TypeScript, Tailwind CSS v4, Spartan UI |
| Testing | xUnit, Testcontainers, Vitest, Playwright + axe |
| Tooling | Docker Compose, GitHub Actions |

## Running locally

**Prerequisites:** .NET 10 SDK, Node.js 24 (with Corepack for pnpm), Docker.

```bash
cp .env.example .env      # change HILOM_DB_PORT here if 5432 is taken
docker compose up -d      # PostgreSQL 18 + Mailpit inbox on http://localhost:8025
```

```bash
cd api && dotnet tool restore && dotnet run --project src/Hilom.Api
```

The API runs on http://localhost:5080. It applies database migrations on startup in development, and the API reference is at http://localhost:5080/scalar.

```bash
cd api && dotnet test     # unit, architecture and integration tests (integration tests need Docker)
```

```bash
cd web && pnpm install && pnpm start
```

The web app runs on http://localhost:4200 and talks to the API through a dev proxy. Sign in with the development administrator: `admin@hilom.test` / `Hilom-Dev-Admin-2026` (local development only).

## Documentation

Design docs live in [`docs/`](docs/README.md):

- [Architecture](docs/ARCHITECTURE.md): modules, main flow, tech choices
- [Data model](docs/DATA_MODEL.md): tables, relations, database-enforced rules
- [Access control](docs/RBAC.md): permissions, scopes, default roles
- [Audit logging](docs/AUDIT.md): event catalog and guarantees
- [Design system](docs/DESIGN_SYSTEM.md): tokens, components, accessibility
- [Conventions](docs/CONVENTIONS.md) and [build plan](docs/BUILD_PLAN.md)
- [Decision records](docs/adr/)

## Repository layout

```
api/      .NET solution (modules: Access, Workforce, Facility, Patients, Scheduling, Clinical)
web/      Angular app (staff app + patient portal)
docs/     Design docs and decision records
deploy/   Production Docker and deploy scripts
```

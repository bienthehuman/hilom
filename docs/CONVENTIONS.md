# Conventions and Tooling

Status: **Accepted (2026-11-09).** These settle every remaining "how" question before phase 0.

## 1. Versions

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10 (LTS) | Pinned in `global.json` |
| PostgreSQL | 18 | Docker image `postgres:18` |
| Node.js | 24 (LTS), at least 24.15 | Pinned in `.nvmrc` and `package.json#engines` (Angular CLI 22.2 needs 24.15+) |
| pnpm | latest stable | Pinned via `packageManager` field (Corepack) |
| Angular | 22 | Upgrade to each new major within a month of release |
| Tailwind CSS | 4 | |
| Spartan UI | 1.x | |

## 2. Repository layout (one repo)

```
hilom/
  api/                    # .NET solution (see ARCHITECTURE.md §5)
    Hilom.slnx              # .NET 10 solution format
    dotnet-tools.json       # local tools (dotnet-ef)
    src/  tests/
    Directory.Build.props   # nullable, warnings-as-errors, analyzers, LangVersion
    Directory.Packages.props # central package versions
  web/                    # Angular workspace (see ARCHITECTURE.md §6)
  docs/                   # these docs
  deploy/                 # Dockerfile, compose files, scripts
  .github/workflows/      # CI
  docker-compose.yml      # postgres + mailpit for local dev
  README.md
```

**Local dev:**
- `docker compose up -d` starts **PostgreSQL 18** and **Mailpit** (catches invite and notification emails at `localhost:8025`).
- `dotnet watch --project api/src/Hilom.Api` runs the API on http://localhost:5080 (API reference at `/scalar`). In development it reads `HILOM_DB_*` from `.env` and applies migrations on startup.
- `pnpm start` (inside `web/`) runs Angular on http://localhost:4200 and proxies `/api` and `/health` to the API.
- Web checks: `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm test:ci` (Vitest), `pnpm e2e` (Playwright + axe, light and dark). `pnpm e2e` starts its own API (port 5180) and dev server (port 4300) against its own database, `hilom_e2e`, which is dropped and recreated at the start of every run, so runs start clean and never touch your development data. It needs the database container up (`docker compose up -d`); your own servers on 5080/4200 can keep running. First run: `pnpm exec playwright install chromium`.
- Email: the API sends through SMTP using the `Email:*` settings. The defaults send to Mailpit (`localhost:1025`, or `HILOM_MAIL_SMTP_PORT`) and build links from `Email:AppUrl` (`http://localhost:4200`). Integration tests swap the sender for an in-memory one.
- Development sign-in: `admin@hilom.test` / `Hilom-Dev-Admin-2026`, seeded on first API start from `appsettings.Development.json`. Development only; deployments set `Access__SeedAdmin__*` from secrets.
- API client: building the API writes `api/openapi/hilom-api.json`; `pnpm api:generate` regenerates `web/src/app/api/` from it. Both are committed, and CI fails if they're out of date.
- Tests use **Microsoft Testing Platform** (opted in via `global.json`): `dotnet test --project api/...` or `dotnet test` inside `api/`. Integration tests start their own PostgreSQL container, so Docker must be running.
- New migration: `dotnet ef migrations add <Name> --project src/Hilom.Infrastructure --startup-project src/Hilom.Api --output-dir Persistence/Migrations` (run inside `api/` after `dotnet tool restore`).

## 3. Backend conventions

| Topic | Rule |
|---|---|
| Routes | `/api/{module}/{resource}`, plural, kebab-case: `/api/clinical/encounters/{id}/vital-signs` |
| Actions that aren't CRUD | Sub-resource verbs as POST: `POST /api/scheduling/appointments/{id}/confirm` |
| IDs | UUID v7 strings |
| Dates | ISO 8601 UTC in JSON (`2026-11-09T06:30:00Z`). Pure dates as `2026-11-09`. |
| Enums | Lower snake-case strings in JSON and the database (`pending_approval`) |
| Lists | `?page=1&pageSize=25&sort=-createdAt,lastName&q=...` → `{ items, total, page, pageSize }`. Max page size 100. |
| Errors | RFC 7807 ProblemDetails with `code` (e.g. `slot_already_booked`) and `errors` (field → messages) |
| Status codes | 200/201/204 · 400 validation · 401 not logged in · 403 missing permission · 404 not found **or out of scope** · 409 conflict (constraint/concurrency) · 422 business rule |
| Concurrency | Editable resources return `version` (xmin). Updates send it back, and a mismatch returns 409. |
| Every endpoint | Exactly one of `.RequirePermission(Perm.X)`, `.RequireSignedIn()` or `.AllowAnonymous()`. State-changing ones also get `.Audited(AuditEvents.X)` or `.NotAudited("reason")`. Both enforced by tests. See AUDIT.md for recording events. |
| Enum query parameters | Same snake_case values as JSON (`?kind=data_change`). Declare the parameter as the enum type; `SnakeCaseEnumQuery` does the mapping. Unknown values return 400 `bad_request`. |
| New permission | Add it to the catalog in docs/RBAC.md, then to `Perm.cs`, then to the default-role table and `DefaultRoles.cs` as needed. Tests compare code and doc. |
| Naming | C#: PascalCase types, `_camelCase` private fields. One feature = one file (`ConfirmAppointment.cs`). |
| Async | `async` all the way. `CancellationToken` passed through every handler. |
| Config | Options pattern, validated at startup. Secrets only from env vars / user-secrets, never committed. |

## 4. Frontend conventions

| Topic | Rule |
|---|---|
| Components | Standalone, `OnPush`, signals for state, `input()` / `output()` functions, new control flow (`@if`, `@for`) |
| State | Signals + services per feature. **No NgRx.** Server data loaded through `resource()` / `httpResource()` where it fits. |
| Forms | **Typed reactive forms** wrapped by `form-field`. Re-evaluate Angular Signal Forms once stable, without mixing both in one feature. |
| API client | Generated from the backend OpenAPI document with **ng-openapi-gen** into `web/src/app/api/`. Never edited by hand. CI fails if regeneration produces a diff. |
| Styling | Tailwind utility classes with **semantic tokens only** (see DESIGN_SYSTEM.md). No component SCSS unless a utility can't do it. |
| Routing | Lazy-loaded feature routes. Every route has `permissionGuard`. |
| Naming | Files kebab-case (`appointment-list.component.ts`). Selector prefix `app-` for features and `ui-` for our design-system components; Spartan components keep `hlm`. |
| Dates | Luxon. Display in hospital time zone, 24h clock. Formatting through shared pipes only. |
| Testing | Vitest for units (services, guards, pipes, status map). Playwright + axe for E2E. |
| Lint/format | angular-eslint, Prettier (with Tailwind class sorting plugin) |

## 5. Git and workflow

- **Trunk-based.** `main` is always green and deployable. Short-lived branches: `feat/…`, `fix/…`, `chore/…`, `docs/…`.
- **One PR per build-plan slice** (see [BUILD_PLAN.md](BUILD_PLAN.md)), even working solo. CI runs on each, and it leaves a readable history for reviewers.
- **Conventional Commits** (`feat(scheduling): confirm appointment`). **Squash merge.**
- **Tag a release at the end of each phase:** `v0.1.0` foundation, `v0.2.0` workforce, …, `v1.0.0` live demo.
- **Protect `main`:** PRs required, CI must pass.

## 6. CI (GitHub Actions)

`.github/workflows/ci.yml` runs on every PR and push to `main`. All four jobs are required checks on `main`.

| Job | Steps |
|---|---|
| **API** | `dotnet format --verify-no-changes` → build in Release (warnings as errors) → unit, architecture and integration tests (Testcontainers PostgreSQL) |
| **Web** | install (frozen lockfile) → Prettier check → lint → typecheck → Vitest → production build |
| **API contract** | build the API (rewrites the OpenAPI document) → regenerate the client → fail if either differs from what's committed |
| **E2E** | Starts PostgreSQL, Mailpit and the API (Development mode: migrate + seed), then runs Playwright in light and dark with axe checks. The invite test reads its email from Mailpit's API. |

A newer push to the same branch cancels the older run.

## 7. Definition of done (every slice)

- [ ] Endpoints have permission + audit metadata, validation, and ProblemDetails errors
- [ ] Integration tests: happy path, missing permission (403), out of scope (404), constraint/conflict (409) where relevant
- [ ] Screens have loading, empty, error and success states, and work in light and dark mode, by keyboard, at tablet width
- [ ] New UI components added to `/design`
- [ ] Docs updated if the data model, permissions, audit events or decisions changed
- [ ] CI green, squash-merged

## 8. Deferred decisions (not needed until later)

| Decision | When | Default leaning |
|---|---|---|
| Hosting provider | Phase 4 | One small VPS running the Docker image + Postgres, or a .NET-friendly PaaS. Depends on cost at the time. |
| Email provider for the live demo | Phase 4 | Keep Mailpit-style capture in the demo so no real emails are sent |
| Charting library (vitals trend, dashboards) | Phase 3.2 | Lightweight option, chosen when the first chart is built |

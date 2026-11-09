# ADR 0005: Cookie session auth instead of JWT in the browser

**Status:** Accepted · **Date:** 2026-11-09

## Context
The Angular app and the API are deployed together under one domain. Clinical data makes token theft costly.

## Decision
- ASP.NET Core Identity's **password hasher** (PBKDF2, upgraded automatically) handles password hashing. Lockout and the security stamp are a few lines on our own `User` entity. *(Updated in slice 0.6: Identity's full user store was not used, because its fixed `AspNetUsers` tables don't fit one schema per module, snake_case naming and UUID v7 keys.)*
- Login issues an `HttpOnly`, `Secure`, `SameSite=Strict` cookie. Angular never sees a token.
- Antiforgery tokens protect state-changing requests. The API issues `XSRF-TOKEN`, Angular's built-in XSRF support echoes it in `X-XSRF-TOKEN`, and a middleware rejects any POST/PUT/PATCH/DELETE under `/api` without a valid token (minimal APIs don't check JSON requests on their own).
- Every endpoint requires a signed-in user unless it explicitly allows anonymous access (fallback authorization policy).
- The session is checked against the database on every request: a disabled user or a changed security stamp (password change) ends it immediately.
- 5 wrong passwords lock an account for 15 minutes; sign-in is also rate-limited per IP address. Unknown emails, wrong passwords and disabled accounts all get the same answer.
- Sessions slide: they end after 8 hours without activity (one shift), configurable.
- In development, the Angular dev server proxies `/api` to keep the same origin.

## Alternatives considered
- **JWT in localStorage:** readable by any injected script. Rejected.
- **JWT + refresh token rotation:** necessary for third-party or mobile clients. We have neither.

## Consequences
- Role changes take effect on the next request (permission cache keyed by `permissions_version`), with no token expiry to wait for.
- If a mobile app is ever added, a token endpoint can be added alongside.

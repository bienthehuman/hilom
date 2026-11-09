# ADR 0005: Cookie session auth instead of JWT in the browser

**Status:** Accepted · **Date:** 2026-11-09

## Context
The Angular app and the API are deployed together under one domain. Clinical data makes token theft costly.

## Decision
- ASP.NET Core Identity handles password hashing, lockout and security stamps.
- Login issues an `HttpOnly`, `Secure`, `SameSite=Strict` cookie. Angular never sees a token.
- Antiforgery tokens protect state-changing requests (Angular's built-in XSRF support reads the cookie and sends the header).
- In development, the Angular dev server proxies `/api` to keep the same origin.

## Alternatives considered
- **JWT in localStorage:** readable by any injected script. Rejected.
- **JWT + refresh token rotation:** necessary for third-party or mobile clients. We have neither.

## Consequences
- Role changes take effect on the next request (permission cache keyed by `permissions_version`), with no token expiry to wait for.
- If a mobile app is ever added, a token endpoint can be added alongside.

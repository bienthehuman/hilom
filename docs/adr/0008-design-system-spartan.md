# ADR 0008: Spartan UI + Tailwind tokens, Clinical Teal, Inter

**Status:** Accepted · **Date:** 2026-11-09

## Context
The three old apps used Bootstrap, jQuery and partial Tailwind with no shared look. The new frontend is Angular 22 + Tailwind v4 and must have one consistent, accessible design system that works in light and dark mode. It should be ours to shape, which also matters for the portfolio.

## Decision
- **Spartan UI** (stable since 1.0 in June 2026): headless accessible primitives (`@spartan-ng/brain`, built on Angular CDK) plus styled components whose source is copied into `web/src/app/ui/` and owned by us.
- **TanStack Table** for data grids, **Lucide** icons through ng-icons, **Luxon** for dates.
- **Semantic tokens** in the shadcn naming convention, so Spartan components work as-is and we add our own (`success`, `warning`, `info`, shift colors).
- **Clinical Teal** brand (teal-700 light / teal-400 dark), slate neutrals, red/green/amber reserved for meaning.
- **Inter Variable**, self-hosted, tabular numbers.
- Light and dark mode from day one.

## Alternatives considered
- **Own components on Angular CDK:** full control, but date pickers, comboboxes and menus would take weeks to build accessibly.
- **PrimeNG (unstyled):** many components, but we'd fight its markup to get our own look.
- **Angular Material:** mature, but its Material 3 look and theming system conflict with Tailwind.
- **Hospital Blue / Indigo palettes:** blue is generic and collides with "info". Indigo reads less clinical.

## Consequences
- We maintain the copied component code. Updates from Spartan are pulled in deliberately, not automatically.
- All colors go through tokens. A re-theme is a single file.

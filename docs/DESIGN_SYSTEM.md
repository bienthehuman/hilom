# Design System

Status: **Accepted (2026-11-09).** See [ADR 0008](adr/0008-design-system-spartan.md).

| Decision | Choice |
|---|---|
| Component foundation | **Spartan UI** (`@spartan-ng/brain` primitives + `helm` styles copied into our repo), **vega** style |
| Data tables | **TanStack Table** (`@tanstack/angular-table`) with our table styles |
| Icons | **Lucide** via `@ng-icons/lucide` |
| Brand color | **Clinical Teal** |
| Neutrals | **Slate** |
| Typeface | **Inter** (variable, self-hosted via `@fontsource-variable/inter`) |
| Themes | **Light and dark from day one.** Follows the system setting, with a manual toggle. |
| Dates/times | **Luxon** (Spartan's date components use it). Shown in the hospital time zone, 24-hour clock. |
| Accessibility target | **WCAG 2.2 AA** |

## 1. Principles

1. **Clarity over decoration.** Staff use this under time pressure. Every screen answers "what needs my attention?" first.
2. **Color never carries meaning alone.** Statuses have text, and abnormal vitals have an arrow icon as well as color.
3. **One meaning per color.** Red always means danger/abnormal, green success, amber needs attention, teal brand/action. No exceptions for decoration.
4. **Tokens only.** Components never use raw hex or Tailwind palette classes (`bg-teal-700`). They use semantic tokens (`bg-primary`). That's what makes dark mode and future re-theming free.
5. **Same thing, same look.** A status, a patient header, or an empty state looks identical in every module.

## 2. Color tokens

Token names follow the shadcn/Spartan convention, so Spartan components work without edits. Our additions are marked ✚.

| Token | Light | Dark | Used for |
|---|---|---|---|
| `background` | slate-50 `#f8fafc` | slate-950 `#020617` | Page |
| `foreground` | slate-900 `#0f172a` | slate-100 `#f1f5f9` | Body text |
| `card` / `popover` | white | slate-900 `#0f172a` | Cards, menus, dialogs |
| `muted` | slate-100 `#f1f5f9` | slate-800 `#1e293b` | Subtle fills, table header, sidebar |
| `muted-foreground` | slate-600 `#475569` | slate-400 `#94a3b8` | Secondary text, labels |
| `border` | slate-200 `#e2e8f0` | slate-800 `#1e293b` | Dividers, card edges (decorative) |
| `input` | slate-500 `#64748b` | slate-500 `#64748b` | Form control borders (must meet 3:1) |
| `ring` | teal-600 `#0d9488` | teal-400 `#2dd4bf` | Focus ring |
| `primary` | teal-700 `#0f766e` | teal-400 `#2dd4bf` | Primary buttons, active nav, links |
| `primary-foreground` | white | slate-950 | Text on primary |
| `accent` | teal-50 `#f0fdfa` | teal-950 `#042f2e` | Hover/selected rows, soft brand fills |
| `accent-foreground` | teal-800 `#115e59` | teal-200 `#99f6e4` | Text on accent |
| `destructive` | red-700 `#b91c1c` | red-400 `#f87171` | Danger buttons, critical, abnormal-high |
| `destructive-foreground` | white | slate-950 | |
| ✚ `destructive-soft` | red-50 | red-950 | Danger pill/alert background |
| ✚ `success` / `success-soft` | green-700 / green-50 | green-400 / green-950 | Completed, stable, given |
| ✚ `warning` / `warning-soft` | amber-700 / amber-50 | amber-400 / amber-950 | Pending, watch, held |
| ✚ `info` / `info-soft` | sky-700 / sky-50 | sky-400 / sky-950 | Requested, informational |
| ✚ `shift-am` | sky-600 | sky-400 | Morning shift chip |
| ✚ `shift-pm` | amber-600 | amber-400 | Afternoon shift chip |
| ✚ `shift-noc` | violet-600 | violet-400 | Night shift chip |

**Verified contrast (WCAG):**

| Pair | Light | Dark |
|---|---|---|
| Body text on background | 17.1 | 18.4 |
| Muted text on card | 7.6 | 7.0 |
| Primary button text | 5.5 | 10.8 |
| Accent foreground on accent | 7.3 | 11.5 |
| Success on success-soft | 4.8 | 8.6 |
| Warning on warning-soft | 4.8 | 9.0 |
| Destructive on destructive-soft | 5.9 | 5.8 |
| Info on info-soft | 5.6 | 6.5 |
| Input border on card (needs 3:1) | 4.8 | 3.7 |

**How it's wired (Tailwind v4):**

```css
/* web/src/styles/tokens.css */
@import "tailwindcss";
@import "tw-animate-css";

@custom-variant dark (&:where(.dark, .dark *));

:root {
  --background: oklch(0.984 0.003 247.858);   /* slate-50 */
  --foreground: oklch(0.208 0.042 265.755);   /* slate-900 */
  --primary: oklch(0.511 0.096 186.391);      /* teal-700 */
  --primary-foreground: oklch(1 0 0);
  /* …every token in the table above… */
}
.dark {
  --background: oklch(0.129 0.042 264.695);   /* slate-950 */
  --primary: oklch(0.777 0.152 181.912);      /* teal-400 */
  /* … */
}

@theme inline {
  --color-background: var(--background);
  --color-primary: var(--primary);
  --color-success: var(--success);
  /* … so components write bg-primary, text-success, border-input … */
}
```

A `ThemeService` adds or removes `.dark` on `<html>`: system preference by default, user override saved per user.

## 3. Status colors (one map for the whole app)

Defined once in `ui/status/status-map.ts`. `<app-status-pill [status]="appointment.status" kind="appointment">` looks the label and tone up there.

| Tone | Appointment | Swap | Order | Patient link | Encounter | MAR outcome | Employee | Reference list | Audit severity |
|---|---|---|---|---|---|---|---|---|---|
| **info** | requested | | | | | | | | notice |
| **primary** | confirmed, checked in | | active | | open | | | | |
| **success** | completed | approved | completed | active | | given | active | active | |
| **warning** | no show | pending target, pending approval | | pending | | held, not available | on leave | | |
| **danger** | declined | declined, rejected | | rejected | | refused | | | critical |
| **neutral** | cancelled | cancelled | discontinued | revoked | closed, voided | | separated | inactive | info |

Allergies use two more kinds: **allergy** (`active` primary; `resolved` and `entered in error` neutral) and **allergy severity** (`mild` info, `moderate` warning, `severe` danger).

**Clinical flags**

| Flag | Look |
|---|---|
| Vital out of normal range | `warning` text + ↑/↓ icon |
| Vital in critical range | `destructive` text, bold + ↑/↓ icon |
| Severe allergy | `destructive` banner in the patient header, always visible |
| Allergy check before a dose | `ui-allergy-warning` (built in 3.8): every active allergy, most severe first. A name match with the medication goes first and makes it `destructive`; otherwise `warning`; none on record is `info` |
| Voided entry | Struck-through, `muted-foreground`, with a "Voided" pill and reason on hover. Allergies entered in error show the reason as text under the entry, so it's readable without a pointer. |

Normal and critical ranges live in one backend config, so the UI and server agree on them.

**Chart colors.** Charts use the `chart-1` … `chart-5` tokens, never status colors for series. A two-series chart (blood pressure) uses `chart-1` (teal-600) and `chart-4` (violet-500) in both themes: that pair passes the palette checks (lightness band, color-blind separation, contrast) on the light and dark card surfaces. Dark mode uses those mid steps rather than the 400s, which are too light against slate-900.

## 4. Typography

- **Inter Variable**, self-hosted. Tabular numbers (`font-variant-numeric: tabular-nums`) are on by default in tables, vitals, times and IDs.
- **Staff app base size is 14px** (dense data). **The patient portal uses 16px** (mobile, less technical users).

| Token | Size / line height | Weight | Use |
|---|---|---|---|
| `text-xs` | 12 / 16 | 500 | Pills, table meta |
| `text-sm` | 13 / 20 | 400 | Table cells, helper text |
| `text-base` | 14 / 20 | 400 | Body (staff) |
| `text-lg` | 16 / 24 | 500 | Card titles, body (portal) |
| `text-xl` | 18 / 28 | 600 | Section headings |
| `text-2xl` | 22 / 30 | 600 | Page titles |
| `text-3xl` | 28 / 36 | 700 | Dashboard numbers |

**Voice:**
- Sentence case everywhere ("Admit patient", not "ADMIT PATIENT" or "Admit Patient").
- Buttons say the action.
- Errors say what happened and what to do.

## 5. Spacing, shape, elevation

- **Spacing:** Tailwind's 4px scale. Page padding 24px (16px on mobile). Card padding 16–20px. Form field gap 16px.
- **Radius:** `sm` 4px (pills inside tables), `md` 6px (buttons, inputs), `lg` 8px (cards), `xl` 12px (dialogs, sheets), `full` (status pills, avatars).
- **Elevation:** borders, not shadows, for cards. Shadows only on things that float (menus, popovers, dialogs, toasts).
- **Control height:** 36px in the staff app, 44px in the portal (comfortable touch targets).
- **Motion:** 150–200ms ease-out for overlays and toasts, using `tw-animate-css`. Everything respects `prefers-reduced-motion`.

## 6. Layout

| Area | Rule |
|---|---|
| Staff app shell | Collapsible sidebar (240px / 48px icons-only; a drawer below 768px; Ctrl/⌘+B) built from permissions. Top bar has the sidebar toggle, global patient search, theme toggle and user menu showing the signed-in name. The search (for anyone with `patients.view`) is a combobox: name or MRN, arrow keys and Enter to open a patient, Enter without a choice for the filtered registry, Ctrl/⌘+K to focus from anywhere. Results respect the user's scope, and only opening a patient counts as a record view. |
| Patient portal shell | Mobile-first, top bar + bottom tabs (Appointments, Patients, Account). Separate route tree, same tokens. |
| Breakpoints | Tailwind defaults. **The staff app is designed for desktop and tablet (≥768px)**, since nurses use tablets on rounds. The portal is designed for 360px and up. |
| Content width | Tables use full width. Forms max 720px. Reading pages (notes, docs) max 72ch. |
| Page anatomy | Page header (title, breadcrumbs, primary action on the right) → filters → content → pagination |

## 7. Components

### From Spartan (adapted to our tokens, in `web/src/app/ui/`)
- **Forms:** button, input, textarea, label, select, combobox (doctor/patient pickers), checkbox, radio group, switch, date picker / calendar
- **Overlays:** dialog, alert dialog (confirmations), sheet (side drawer for quick edits), popover, dropdown menu, tooltip, toast (sonner)
- **Layout and display:** card, tabs, separator, avatar, breadcrumb, skeleton, alert, sidebar, pagination

### Our own (built from the above)
| Component | Purpose |
|---|---|
| `data-table` | TanStack Table wrapper: server-side sort/filter/paginate, loading skeleton, empty state, row actions, sticky header |
| `form-field` | Label + control + hint + error, wired to Angular typed reactive forms, with `aria-describedby` |
| `status-pill` | Reads the status map in §3 |
| `page-header` | Title, breadcrumbs, actions |
| `empty-state` | Icon, message, optional action |
| `patient-header` | Name, MRN, age/sex, current ward/bed, allergy banner. Shown on every patient/encounter screen. |
| `vital-value` | Number + unit + range flag + icon. Out of range: `warning` + arrow; critical: `destructive`, bold + arrow. The direction is also read out ("high", "critically low"). |
| `trend-chart` | Small SVG line chart of readings over time: one axis, a shaded normal band, status rings on out-of-range points, crosshair tooltip on hover and by keyboard (arrow keys). Always shown next to a table of the same readings. |
| `shift-chip` | Shift code with its shift color |
| `bed-tile` | Census board tile: bed, patient (name, MRN, attending physician, length of stay), flags. Severe allergies ("Allergy: Penicillin") and critical vital signs in the latest set ("Critical: Heart rate") use `destructive` with an icon and words, and give the tile a red edge. A free bed is dashed on `muted`. When the viewer can open the stay, the name is a link and the whole tile is its click target. The board lays tiles out in a grid per ward (at least 13rem wide each), so tablets get two to four across. |
| `bed-picker` | Choose one bed, grouped by ward. Native radio buttons styled as tiles (at least 44px tall): Tab enters a ward, the arrow keys move between its beds, and each bed is read out with its ward ("W3, Bed 2"). The chosen bed uses `primary`. Callers pass only the beds that can be chosen; admitting and transferring pass the free ones. |
| `permission-matrix` | Role editor grid: permission × scope radio |
| `time-range` | Formats ranges in the hospital time zone ("Mon 12 Nov, 08:00–08:30") |
| `confirm-action` | Directive (`uiConfirmAction`) on a button: opens an alert dialog and runs the action on confirm. With `requireReason` the reason is a required field and is passed to the action (voids, discharges). Stays open and shows the error if the action fails. |
| `voided-pill` | Neutral "Voided" pill for an entry voided in error. The reason and who voided it show on hover and keyboard focus, and are read out with the pill. The entry beside it is struck through in `muted-foreground`. |

### Every data screen has four states
**Loading** (skeleton, not spinner), **empty** (`empty-state` with next action), **error** (inline alert with retry), **success**. This is part of review, not optional.

## 8. Accessibility checklist

- Visible focus on every interactive element. Never `outline: none` without a replacement.
  - **Text inputs, text areas, selects:** the border turns `ring` teal with a 1px ring flush against it (reads as a 2px border). No halo, no gap.
  - **Buttons, links, checkboxes, switches:** a 2px `ring` outline or halo, since a border change alone is too subtle on small or borderless controls.
  - **Invalid fields:** red (`destructive`) border, no halo.
- Every enabled button, menu item and select option shows the pointer (hand) cursor; disabled buttons show not-allowed.
- Every input has a visible label. Placeholder text isn't a label.
- Everything works by keyboard. Dialogs trap focus and return it on close (CDK/Spartan handle this).
- Toasts and validation summaries use `aria-live`.
- Minimum target size 24×24px (staff) and 44×44px (portal).
- Playwright runs **axe** on every page in E2E. Serious or critical violations fail CI.

## 9. Living style guide

**A `/design` route** (dev and demo builds only) shows every token, both themes, every component in every state, and the status map. New components are added there in the same PR that creates them.

## 10. Implementation (as built in slice 0.5)

**Where things live**

| Path | Contents | Import as |
|---|---|---|
| `web/src/app/ui/<name>/` | Spartan components (generated, then owned and edited by us) | `@hilom/ui/<name>`, e.g. `@hilom/ui/button` |
| `web/src/app/ui/<name>/` | Our components: `status`, `page-header`, `empty-state`, `form-field`, `data-table`, `theme`, `theme-toggle` | same |
| `web/src/styles/tokens.css` | Every color, radius and type token | — |
| `web/components.json` | Spartan CLI settings (path, alias, style) | — |

Add a Spartan component with `pnpm ng g @spartan-ng/cli:ui <name>` inside `web/`.

**Style choice:** Spartan's **vega** style. Its default controls are 36px tall, matching §5. The more compact styles (28–32px) were too small for comfortable clinical use, and the rounder ones read less clinical.

**Changes we made to generated components**

| Component | Change | Why |
|---|---|---|
| Button | Solid `destructive` variant; primary hover at 90% instead of 80% | Danger needs to stand out; text keeps AA contrast on hover |
| Card | Border instead of ring + shadow; 8px radius | §5: borders for cards, shadows only for floating things |
| Alert | `success`, `warning`, `info` variants; soft tinted backgrounds | Severe-allergy banner and other persistent messages (§3) |
| Toast | 12px radius | Same radius as dialogs |
| Pagination | Removed client-side numbered pagination | Tables page on the server |
| Input, textarea, select trigger | Focus: teal border + 1px flush ring instead of a 3px translucent halo; invalid: red border without halo | The halo read as extra padding around fields |
| Dropdown and select items, checkbox | `cursor-pointer` instead of `cursor-default` | Everything clickable looks clickable |

**Rules learned while building**

- **Shift colors are for dots and bars, never text backgrounds.** White text on amber-600 or sky-600 fails contrast. Shift chips use a colored dot next to normal text.
- **Data table sorting is opt-in per column** (`enableSorting: true`). The table sets `defaultColumn: { enableSorting: false }`. A table-level `enableSorting: false` would switch sorting off for every column.
- **`ui-form-field` carries Spartan's field directive on its own element.** That's what lets the projected input (declared in the parent template) find it, which links the label, hint and error for screen readers.
- **Keep heavy components out of the startup bundle.** Import what the app shell needs from small entry points (that's why `theme` and `theme-toggle` are separate), and `@defer` anything not needed for first paint (the toaster). Defer only works when the component class itself is in `imports`, not a Spartan `…Imports` array. The startup bundle budget is 500 kB.
- **Dialog, sheet and alert-dialog backdrops use `bg-black/10`.** It's the one place a raw color is allowed: a dim backdrop that's the same in both themes.


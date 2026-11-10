import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Top of every page: optional breadcrumbs, the page title (the only h1), a short description,
 * an optional meta line (record number, status), and the page's primary actions on the right.
 *
 * ```html
 * <ui-page-header title="Ward census" description="Patients currently admitted">
 *   <nav breadcrumbs>…</nav>
 *   <div meta>EMP-2026-0042 · <ui-status-pill kind="employee" status="active" /></div>
 *   <button actions hlmBtn>Admit patient</button>
 * </ui-page-header>
 * ```
 */
@Component({
  selector: 'ui-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-2 pb-6' },
  template: `
    <ng-content select="[breadcrumbs]" />
    <div class="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
      <div class="min-w-0">
        <h1 class="text-2xl font-semibold tracking-tight text-foreground">{{ title() }}</h1>
        @if (description()) {
          <p class="mt-1 max-w-[72ch] text-muted-foreground">{{ description() }}</p>
        }
        <ng-content select="[meta]" />
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <ng-content select="[actions]" />
      </div>
    </div>
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly description = input<string>();
}

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { PageHeader } from '@hilom/ui/page-header';

import { LookupList } from './lookup-list';
import { LOOKUP_KINDS, LOOKUP_KIND_LIST, type LookupKindId } from './lookup-kinds';

/** Departments, positions and specializations: one tab each, all using the same list. */
@Component({
  selector: 'app-lookups-page',
  imports: [RouterLink, RouterLinkActive, PageHeader, LookupList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <ui-page-header
        title="Reference lists"
        description="The choices offered on employee records. Deactivate entries you no longer use; they're never deleted."
      />

      <nav aria-label="Reference lists" class="mb-6 border-b">
        <ul class="-mb-px flex gap-1 overflow-x-auto">
          @for (item of kinds; track item.id) {
            <li>
              <a
                [routerLink]="['/workforce/lists', item.id]"
                routerLinkActive="is-active"
                ariaCurrentWhenActive="page"
                class="inline-flex h-10 items-center border-b-2 border-transparent px-3 text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors hover:text-foreground focus-visible:rounded-sm focus-visible:outline-2 focus-visible:outline-ring aria-[current=page]:border-primary aria-[current=page]:text-foreground"
              >
                {{ item.plural }}
              </a>
            </li>
          }
        </ul>
      </nav>

      <p class="mb-4 text-sm text-muted-foreground">{{ current().description }}</p>

      <!-- Recreate the list per tab so search, filter and paging start fresh. -->
      @for (k of [current()]; track k.id) {
        <app-lookup-list [kind]="k" />
      }
    </div>
  `,
})
export class LookupsPage {
  /** From the route (`/workforce/lists/:kind`); the route only matches known kinds. */
  readonly kind = input.required<LookupKindId>();

  protected readonly kinds = LOOKUP_KIND_LIST;
  protected readonly current = computed(() => LOOKUP_KINDS[this.kind()]);
}

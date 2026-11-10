import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDropdownMenuImports } from '@hilom/ui/dropdown-menu';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideArchiveRestore,
  lucideEllipsis,
  lucidePencil,
} from '@ng-icons/lucide';

import { type LookupResponse } from '../../../api/models/lookup-response';

/** Active, or hidden from pickers. */
@Component({
  selector: 'app-lookup-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="lookup" [status]="item().isActive ? 'active' : 'inactive'" />`,
})
export class LookupStatusCell {
  readonly item = input.required<LookupResponse>();
}

/** Row menu: rename, and deactivate or reactivate. */
@Component({
  selector: 'app-lookup-actions-cell',
  imports: [HlmButtonImports, HlmDropdownMenuImports, NgIcon],
  providers: [provideIcons({ lucideEllipsis, lucidePencil, lucideArchive, lucideArchiveRestore })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [attr.aria-label]="'Actions for ' + item().name"
      [hlmDropdownMenuTrigger]="menu"
      align="end"
    >
      <ng-icon name="lucideEllipsis" aria-hidden="true" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <button hlmDropdownMenuItem type="button" (triggered)="rename.emit(item())">
          <ng-icon name="lucidePencil" aria-hidden="true" />Rename
        </button>
        <hlm-dropdown-menu-separator />
        @if (item().isActive) {
          <button hlmDropdownMenuItem type="button" (triggered)="deactivate.emit(item())">
            <ng-icon name="lucideArchive" aria-hidden="true" />Deactivate
          </button>
        } @else {
          <button hlmDropdownMenuItem type="button" (triggered)="activate.emit(item())">
            <ng-icon name="lucideArchiveRestore" aria-hidden="true" />Reactivate
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class LookupActionsCell {
  readonly item = input.required<LookupResponse>();

  readonly rename = output<LookupResponse>();
  readonly deactivate = output<LookupResponse>();
  readonly activate = output<LookupResponse>();
}

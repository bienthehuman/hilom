import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { HlmButtonImports } from '@hilom/ui/button';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePanelRightOpen } from '@ng-icons/lucide';

import { type AuditEntryResponse } from '../../../api/models/audit-entry-response';

/** Who acted: the user, the email typed on a failed sign-in, or the system. */
@Component({
  selector: 'app-audit-actor-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (entry().userName) {
      <div class="flex flex-col">
        <span class="font-medium text-foreground">{{ entry().userName }}</span>
        <span class="text-xs text-muted-foreground">{{ entry().userEmail }}</span>
      </div>
    } @else if (entry().attemptedIdentifier) {
      <div class="flex flex-col">
        <span class="text-foreground">{{ entry().attemptedIdentifier }}</span>
        <span class="text-xs text-muted-foreground">Typed at sign-in</span>
      </div>
    } @else {
      <span class="text-muted-foreground">System</span>
    }
  `,
})
export class AuditActorCell {
  readonly entry = input.required<AuditEntryResponse>();
}

/** What happened, in words, with the stable event key underneath. */
@Component({
  selector: 'app-audit-event-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col">
      <span class="text-foreground">{{ title() }}</span>
      <span class="font-mono text-xs text-muted-foreground">{{ entry().event }}</span>
    </div>
  `,
})
export class AuditEventCell {
  readonly entry = input.required<AuditEntryResponse>();
  protected readonly title = computed(() => {
    const entry = this.entry();
    return entry.kind === 'data_change'
      ? `${entry.entityType ?? 'Record'} ${operationOf(entry)}`
      : entry.label;
  });
}

/** Severity and outcome pills side by side. */
@Component({
  selector: 'app-audit-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap gap-1.5">
      <ui-status-pill kind="auditSeverity" [status]="entry().severity" />
      @if (entry().outcome !== 'success') {
        <ui-status-pill kind="auditOutcome" [status]="entry().outcome" />
      }
    </div>
  `,
})
export class AuditStatusCell {
  readonly entry = input.required<AuditEntryResponse>();
}

@Component({
  selector: 'app-audit-open-cell',
  imports: [HlmButtonImports, NgIcon],
  providers: [provideIcons({ lucidePanelRightOpen })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [attr.aria-label]="'Details of ' + entry().label + ' at ' + time()"
      (click)="open.emit(entry())"
    >
      <ng-icon name="lucidePanelRightOpen" aria-hidden="true" />
    </button>
  `,
})
export class AuditOpenCell {
  readonly entry = input.required<AuditEntryResponse>();
  readonly time = input.required<string>();
  readonly open = output<AuditEntryResponse>();
}

/** "created", "changed" or "deleted" for a data-change entry. */
export function operationOf(entry: AuditEntryResponse): string {
  const operation = (entry.details as { operation?: string } | null)?.operation;
  return operation === 'added' ? 'created' : operation === 'deleted' ? 'deleted' : 'changed';
}

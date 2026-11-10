import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusPill } from '@hilom/ui/status';

import { type EmployeeSummaryResponse } from '../../../api/models/employee-summary-response';
import { type LookupRef } from '../../../api/models/lookup-ref';
import { formatListName } from './employee-labels';

/** Name linking to the profile, with the employee number underneath. */
@Component({
  selector: 'app-employee-name-cell',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col">
      <a
        class="font-medium text-foreground underline-offset-4 hover:underline focus-visible:underline"
        [routerLink]="['/workforce/employees', employee().id]"
        >{{ name() }}</a
      >
      <span class="text-xs text-muted-foreground" data-numeric>{{ employee().employeeNo }}</span>
    </div>
  `,
})
export class EmployeeNameCell {
  readonly employee = input.required<EmployeeSummaryResponse>();
  protected readonly name = computed(() => formatListName(this.employee()));
}

/** A department or position, marked when it has since been deactivated. */
@Component({
  selector: 'app-lookup-ref-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    {{ ref().name }}
    @if (!ref().isActive) {
      <span class="text-xs text-muted-foreground">(inactive)</span>
    }
  `,
})
export class LookupRefCell {
  readonly ref = input.required<LookupRef>();
}

@Component({
  selector: 'app-employee-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="employee" [status]="status()" />`,
})
export class EmployeeStatusCell {
  readonly status = input.required<string>();
}

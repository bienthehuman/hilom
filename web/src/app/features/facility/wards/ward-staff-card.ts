import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';

import { Api } from '../../../api/api';
import { listWardAssignments } from '../../../api/fn/workforce/list-ward-assignments';
import { type AssignmentResponse } from '../../../api/models/assignment-response';
import { AuthService } from '../../../core/auth/auth.service';
import { formatDateSpan, hospitalToday } from '../../../core/format/date-time';
import { labelOf } from '../../../core/format/options';
import { PROFESSIONAL_TYPES } from '../../workforce/employees/employee-labels';

/** Who is assigned to the ward today. Assignments are made from the employee's profile. */
@Component({
  selector: 'app-ward-staff-card',
  imports: [RouterLink, HlmButtonImports, HlmCardImports, HlmSkeletonImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard aria-labelledby="staff-heading">
      <div hlmCardHeader>
        <h2 id="staff-heading" hlmCardTitle>Assigned staff</h2>
        <p hlmCardDescription>On this ward today. Assign people from their employee profile.</p>
      </div>
      <div hlmCardContent>
        @if (staff.error()) {
          <p class="text-sm text-destructive" role="alert">
            Couldn't load assigned staff.
            <button hlmBtn variant="link" size="sm" type="button" (click)="staff.reload()">
              Try again
            </button>
          </p>
        } @else if (staff.isLoading() && !staff.hasValue()) {
          <div hlmSkeleton class="h-12 w-full rounded-md" aria-busy="true"></div>
        } @else if (rows().length === 0) {
          <p class="text-sm text-muted-foreground">No one is assigned to this ward today.</p>
        } @else {
          <ul class="flex flex-col divide-y" aria-label="Assigned staff">
            @for (a of rows(); track a.id) {
              <li
                class="flex flex-wrap items-center justify-between gap-2 py-2.5 first:pt-0 last:pb-0"
              >
                @if (canSeeEmployees()) {
                  <a
                    class="font-medium underline-offset-4 hover:underline focus-visible:underline"
                    [routerLink]="['/workforce/employees', a.employee.id]"
                    >{{ name(a) }}</a
                  >
                } @else {
                  <span class="font-medium">{{ name(a) }}</span>
                }
                <span class="text-sm text-muted-foreground" data-numeric>
                  {{ type(a) }} · {{ span(a) }}
                </span>
              </li>
            }
          </ul>
        }
      </div>
    </section>
  `,
})
export class WardStaffCard {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);

  readonly wardId = input.required<string>();

  protected readonly canSeeEmployees = computed(() => this.auth.can('workforce.employees.view'));

  protected readonly staff = resource({
    params: () => this.wardId(),
    loader: ({ params }) =>
      this.api.invoke(listWardAssignments, { wardId: params, on: hospitalToday(), pageSize: 100 }),
  });
  protected readonly rows = computed(() =>
    this.staff.hasValue()
      ? [...this.staff.value().items].sort((a, b) =>
          a.employee.lastName.localeCompare(b.employee.lastName),
        )
      : [],
  );

  protected readonly name = (a: AssignmentResponse) =>
    `${a.employee.lastName}, ${a.employee.firstName}`;
  protected readonly type = (a: AssignmentResponse) =>
    labelOf(PROFESSIONAL_TYPES, a.employee.professionalType);
  protected readonly span = (a: AssignmentResponse) => formatDateSpan(a.startsOn, a.lastDay);
}

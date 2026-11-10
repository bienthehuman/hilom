import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCardImports } from '@hilom/ui/card';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBedDouble } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { listWards } from '../../../api/fn/facility/list-wards';
import { assignWard } from '../../../api/fn/workforce/assign-ward';
import { endWardAssignment } from '../../../api/fn/workforce/end-ward-assignment';
import { listWardAssignments } from '../../../api/fn/workforce/list-ward-assignments';
import { type AssignmentResponse } from '../../../api/models/assignment-response';
import { type EmployeeResponse } from '../../../api/models/employee-response';
import { AuthService } from '../../../core/auth/auth.service';
import {
  dayBefore,
  formatDate,
  formatDateSpan,
  hospitalToday,
} from '../../../core/format/date-time';
import { type Problem, toProblem } from '../../../core/http/problem';

/**
 * The employee's ward over time. The current ward decides which patients they can see, so every change is a
 * critical audit event. Shown to people who can see assignments; changes need `workforce.assignments.manage`.
 */
@Component({
  selector: 'app-employee-ward-card',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    FormField,
    StatusPill,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSelectImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideBedDouble })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard aria-labelledby="ward-heading">
      <div hlmCardHeader class="flex flex-wrap items-start justify-between gap-3">
        <div class="flex flex-col gap-1">
          <h2 id="ward-heading" hlmCardTitle>Ward assignment</h2>
          <p hlmCardDescription>
            @if (current(); as c) {
              On {{ c.ward.code }} · {{ c.ward.name }}
              {{ c.lastDay ? 'until ' + date(c.lastDay) : '' }}
            } @else {
              Not assigned to a ward today.
            }
          </p>
        </div>
        @if (canManage() && employee().status !== 'separated') {
          <button hlmBtn variant="outline" type="button" (click)="openAssign()">
            <ng-icon name="lucideBedDouble" aria-hidden="true" data-icon="inline-start" />Assign to
            ward
          </button>
        }
      </div>
      <div hlmCardContent>
        @if (assignments.error()) {
          <p class="text-sm text-destructive" role="alert">
            Couldn't load ward assignments.
            <button hlmBtn variant="link" size="sm" type="button" (click)="assignments.reload()">
              Try again
            </button>
          </p>
        } @else if (assignments.isLoading() && !assignments.hasValue()) {
          <div hlmSkeleton class="h-12 w-full rounded-md" aria-busy="true"></div>
        } @else if (rows().length === 0) {
          <p class="text-sm text-muted-foreground">No ward assignments yet.</p>
        } @else {
          <ul class="flex flex-col divide-y" aria-label="Ward assignments">
            @for (a of rows(); track a.id) {
              <li
                class="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
              >
                <div class="flex min-w-0 flex-col gap-0.5">
                  <div class="flex flex-wrap items-center gap-2">
                    @if (canSeeWards()) {
                      <a
                        class="font-medium underline-offset-4 hover:underline focus-visible:underline"
                        [routerLink]="['/facility/wards', a.ward.id]"
                        >{{ a.ward.code }} · {{ a.ward.name }}</a
                      >
                    } @else {
                      <span class="font-medium">{{ a.ward.code }} · {{ a.ward.name }}</span>
                    }
                    <ui-status-pill kind="assignment" [status]="a.state" />
                  </div>
                  <span class="text-xs text-muted-foreground" data-numeric>
                    {{ span(a) }}{{ a.reason ? ' · ' + a.reason : '' }}
                  </span>
                </div>
                @if (canManage() && a.state !== 'past') {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    type="button"
                    [attr.aria-label]="'End assignment to ' + a.ward.code"
                    (click)="openEnd(a)"
                  >
                    End
                  </button>
                }
              </li>
            }
          </ul>
        }
      </div>
    </section>

    <!-- Assign -->
    <hlm-dialog [state]="assignOpen() ? 'open' : 'closed'" (closed)="assignOpen.set(false)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Assign {{ employee().firstName }} to a ward</h2>
          <p hlmDialogDescription>
            From the start date they see that ward's patients and roster. Staff are on one ward at a
            time.
          </p>
        </hlm-dialog-header>
        <form
          id="assign-form"
          class="grid gap-4 sm:grid-cols-2"
          [formGroup]="assignForm"
          (ngSubmit)="assign()"
          novalidate
        >
          <ui-form-field class="sm:col-span-2" label="Ward" for="assign-ward" required>
            <hlm-select formControlName="wardId" [itemToString]="wardLabel">
              <hlm-select-trigger buttonId="assign-ward" class="w-full">
                <hlm-select-value placeholder="Choose a ward" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (w of wardOptions(); track w.id) {
                  <hlm-select-item [value]="w.id">{{ w.code }} · {{ w.name }}</hlm-select-item>
                } @empty {
                  <p class="px-2 py-1.5 text-sm text-muted-foreground">No active wards.</p>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>
          <ui-form-field label="Starts on" for="assign-starts" required>
            <input hlmInput id="assign-starts" type="date" formControlName="startsOn" />
          </ui-form-field>
          <ui-form-field label="Last day" for="assign-last" hint="Leave empty if open-ended">
            <input hlmInput id="assign-last" type="date" formControlName="lastDay" />
          </ui-form-field>
          <ui-form-field class="sm:col-span-2" label="Reason" for="assign-reason">
            <input
              hlmInput
              id="assign-reason"
              formControlName="reason"
              autocomplete="off"
              placeholder="e.g. Staffing, rotation"
            />
          </ui-form-field>
          @if (assignProblem(); as p) {
            <p class="text-sm text-destructive sm:col-span-2" role="alert">{{ p.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="assignOpen.set(false)">
            Cancel
          </button>
          <button hlmBtn type="submit" form="assign-form" [disabled]="saving()">
            {{ saving() ? 'Assigning…' : 'Assign' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>

    <!-- End -->
    <hlm-dialog [state]="ending() ? 'open' : 'closed'" (closed)="ending.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-sm">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>End assignment to {{ ending()?.ward?.code }}</h2>
          <p hlmDialogDescription>
            They lose access to the ward's patients after their last day. To move them to another
            ward starting today, choose yesterday.
          </p>
        </hlm-dialog-header>
        <form id="end-form" [formGroup]="endForm" (ngSubmit)="end()" novalidate>
          <ui-form-field label="Last day on the ward" for="end-last" required>
            <input hlmInput id="end-last" type="date" formControlName="lastDay" />
          </ui-form-field>
          @if (endProblem(); as p) {
            <p class="mt-3 text-sm text-destructive" role="alert">{{ p.detail }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="ending.set(null)">Cancel</button>
          <button hlmBtn type="submit" form="end-form" [disabled]="saving()">
            {{ saving() ? 'Saving…' : 'End assignment' }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class EmployeeWardCard {
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder).nonNullable;

  readonly employee = input.required<EmployeeResponse>();

  protected readonly canManage = computed(() => this.auth.can('workforce.assignments.manage'));
  protected readonly canSeeWards = computed(() => this.auth.can('facility.wards.view'));

  protected readonly assignments = resource({
    params: () => this.employee().id,
    loader: ({ params }) =>
      this.api.invoke(listWardAssignments, { employeeId: params, pageSize: 100 }),
  });
  protected readonly rows = computed(() =>
    this.assignments.hasValue() ? this.assignments.value().items : [],
  );
  protected readonly current = computed(() => this.rows().find((a) => a.state === 'current'));

  protected readonly date = formatDate;
  protected readonly span = (a: AssignmentResponse) => formatDateSpan(a.startsOn, a.lastDay);

  protected readonly saving = signal(false);

  // ---- Assign ----
  private readonly wards = resource({
    params: () => (this.assignOpen() ? true : undefined),
    loader: () => this.api.invoke(listWards, { pageSize: 100, status: 'active' }),
  });
  protected readonly wardOptions = computed(() =>
    this.wards.hasValue() ? this.wards.value().items : [],
  );
  protected readonly wardLabel = (id: string) => {
    const ward = this.wardOptions().find((w) => w.id === id);
    return ward ? `${ward.code} · ${ward.name}` : '';
  };

  protected readonly assignOpen = signal(false);
  protected readonly assignProblem = signal<Problem | null>(null);
  protected readonly assignForm = this.fb.group({
    wardId: ['', Validators.required],
    startsOn: ['', Validators.required],
    lastDay: [''],
    reason: ['', Validators.maxLength(300)],
  });

  protected openAssign(): void {
    this.assignForm.reset({ wardId: '', startsOn: hospitalToday(), lastDay: '', reason: '' });
    this.assignProblem.set(null);
    this.assignOpen.set(true);
  }

  protected async assign(): Promise<void> {
    this.assignForm.markAllAsTouched();
    if (this.assignForm.invalid || this.saving()) {
      return;
    }

    const value = this.assignForm.getRawValue();
    this.saving.set(true);
    this.assignProblem.set(null);
    try {
      const saved = await this.api.invoke(assignWard, {
        body: {
          employeeId: this.employee().id,
          wardId: value.wardId,
          startsOn: value.startsOn,
          lastDay: value.lastDay || null,
          reason: value.reason.trim() || null,
        },
      });
      this.assignOpen.set(false);
      toast.success('Assigned to ward', {
        description: `${this.employee().firstName} is on ${saved.ward.code} ${formatDateSpan(saved.startsOn, saved.lastDay).toLowerCase()}.`,
      });
      this.assignments.reload();
    } catch (error) {
      this.assignProblem.set(this.placeErrors(toProblem(error), this.assignForm));
    } finally {
      this.saving.set(false);
    }
  }

  // ---- End ----
  protected readonly ending = signal<AssignmentResponse | null>(null);
  protected readonly endProblem = signal<Problem | null>(null);
  protected readonly endForm = this.fb.group({ lastDay: ['', Validators.required] });

  protected openEnd(assignment: AssignmentResponse): void {
    const today = hospitalToday();
    // An upcoming assignment that's being cancelled can only end on or after its start.
    const suggested = assignment.startsOn > today ? assignment.startsOn : dayBefore(today);
    this.endForm.reset({
      lastDay: suggested < assignment.startsOn ? assignment.startsOn : suggested,
    });
    this.endProblem.set(null);
    this.ending.set(assignment);
  }

  protected async end(): Promise<void> {
    const assignment = this.ending();
    this.endForm.markAllAsTouched();
    if (!assignment || this.endForm.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.endProblem.set(null);
    try {
      const saved = await this.api.invoke(endWardAssignment, {
        id: assignment.id,
        body: { lastDay: this.endForm.getRawValue().lastDay, version: assignment.version },
      });
      this.ending.set(null);
      toast.success('Assignment ended', {
        description: `Last day on ${saved.ward.code}: ${formatDate(saved.lastDay)}.`,
      });
      this.assignments.reload();
    } catch (error) {
      this.endProblem.set(this.placeErrors(toProblem(error), this.endForm));
    } finally {
      this.saving.set(false);
    }
  }

  /** Puts field errors next to their fields; returns the problem to show when none apply. */
  private placeErrors(
    problem: Problem,
    form: { get(path: string): { setErrors(e: object): void } | null },
  ): Problem | null {
    let placed = false;
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const control = form.get(field);
      if (control) {
        control.setErrors({ server: messages[0] });
        placed = true;
      }
    }
    if (placed) {
      return null;
    }
    return problem.code === 'concurrency_conflict'
      ? {
          ...problem,
          detail: 'Someone else changed this assignment just now. Close and try again.',
        }
      : problem;
  }
}

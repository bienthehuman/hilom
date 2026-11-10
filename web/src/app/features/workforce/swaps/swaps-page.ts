import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { formatShiftHours } from '@hilom/ui/shift-chip';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeftRight, lucideCircleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { acceptSwap } from '../../../api/fn/workforce/accept-swap';
import { cancelSwap } from '../../../api/fn/workforce/cancel-swap';
import { declineSwap } from '../../../api/fn/workforce/decline-swap';
import { getMySchedule } from '../../../api/fn/workforce/get-my-schedule';
import { listMySwaps } from '../../../api/fn/workforce/list-my-swaps';
import { listSwapOptions } from '../../../api/fn/workforce/list-swap-options';
import { requestSwap } from '../../../api/fn/workforce/request-swap';
import { type SwapResponse } from '../../../api/models/swap-response';
import { addDays, formatLongDay, hospitalToday } from '../../../core/format/date-time';
import { type Problem, toProblem } from '../../../core/http/problem';
import { SwapCard } from './swap-card';

const AHEAD_DAYS = 30;

interface ShiftLike {
  dutyDate: string;
  shift: { code: string; name: string; startsAt: string; endsAt: string; crossesMidnight: boolean };
}

/** "Thursday, November 12 · AM 06:00–14:00" */
function describe(entry: ShiftLike): string {
  const s = entry.shift;
  return `${formatLongDay(entry.dutyDate)} · ${s.code} ${formatShiftHours(s.startsAt, s.endsAt, s.crossesMidnight)}`;
}

/**
 * Your shift swaps: requests waiting for your answer, your own requests, and history. Offer one of your
 * upcoming shifts for a colleague's on the same ward; they accept, then a head nurse approves.
 */
@Component({
  selector: 'app-swaps-page',
  imports: [
    ReactiveFormsModule,
    NgIcon,
    PageHeader,
    EmptyState,
    FormField,
    SwapCard,
    HlmAlertImports,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideArrowLeftRight, lucideCircleAlert })],
  templateUrl: './swaps-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SwapsPage {
  private readonly api = inject(Api);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly swaps = resource({
    loader: () => this.api.invoke(listMySwaps, { pageSize: 100 }),
  });
  private readonly all = computed(() => (this.swaps.hasValue() ? this.swaps.value().items : []));
  protected readonly waitingForMe = computed(() =>
    this.all().filter((s) => s.youAre === 'target' && s.status === 'pending_target'),
  );
  protected readonly others = computed(() =>
    this.all().filter((s) => !(s.youAre === 'target' && s.status === 'pending_target')),
  );
  protected readonly isOpen = (s: SwapResponse) =>
    s.status === 'pending_target' || s.status === 'pending_approval';

  protected readonly busy = signal(false);

  // ---- Answer and cancel ----
  protected async accept(swap: SwapResponse): Promise<void> {
    await this.run(
      () =>
        this.api.invoke(acceptSwap, { id: swap.id, body: { note: null, version: swap.version } }),
      'Swap accepted',
      'It now needs a head nurse to approve it.',
    );
  }

  protected async cancel(swap: SwapResponse): Promise<void> {
    await this.run(
      () =>
        this.api.invoke(cancelSwap, { id: swap.id, body: { note: null, version: swap.version } }),
      'Swap cancelled',
      'You keep your shift.',
    );
  }

  protected readonly declining = signal<SwapResponse | null>(null);
  protected readonly declineForm = this.fb.group({ note: ['', Validators.maxLength(300)] });

  protected openDecline(swap: SwapResponse): void {
    this.declineForm.reset();
    this.declining.set(swap);
  }

  protected async decline(): Promise<void> {
    const swap = this.declining();
    if (!swap || this.declineForm.invalid) {
      return;
    }
    this.declining.set(null);
    await this.run(
      () =>
        this.api.invoke(declineSwap, {
          id: swap.id,
          body: { note: this.declineForm.getRawValue().note.trim() || null, version: swap.version },
        }),
      'Swap declined',
      `${swap.requester.employee.firstName} keeps their shift.`,
    );
  }

  private async run(
    action: () => Promise<unknown>,
    title: string,
    description: string,
  ): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      toast.success(title, { description });
    } catch (error) {
      toast.error("Couldn't update the swap", {
        description: toProblem(error).detail ?? 'Please try again.',
      });
    } finally {
      this.busy.set(false);
      this.swaps.reload();
    }
  }

  // ---- Request ----
  protected readonly requestOpen = signal(false);
  protected readonly requestProblem = signal<Problem | null>(null);
  protected readonly requestForm = this.fb.group({
    mine: ['', Validators.required],
    theirs: ['', Validators.required],
    message: ['', Validators.maxLength(300)],
  });
  private readonly chosenMine = signal('');

  /** Your shifts from today to a month ahead. */
  private readonly myShifts = resource({
    params: () => (this.requestOpen() ? hospitalToday() : undefined),
    loader: ({ params }) =>
      this.api.invoke(getMySchedule, { from: params, to: addDays(params, AHEAD_DAYS) }),
  });
  protected readonly myOptions = computed(() =>
    this.myShifts.hasValue()
      ? this.myShifts.value().entries.filter((e) => e.status === 'scheduled')
      : [],
  );
  protected readonly myShiftsLoading = computed(() => this.myShifts.isLoading());

  protected readonly options = resource({
    params: () => this.chosenMine() || undefined,
    loader: ({ params }) => this.api.invoke(listSwapOptions, { entryId: params }),
  });
  protected readonly theirOptions = computed(() =>
    this.options.hasValue() ? this.options.value() : [],
  );

  protected readonly mineLabel = (id: string) => {
    const entry = this.myOptions().find((e) => e.id === id);
    return entry ? describe(entry) : '';
  };
  protected readonly theirsLabel = (id: string) => {
    const option = this.theirOptions().find((o) => o.entryId === id);
    return option
      ? `${option.employee.firstName} ${option.employee.lastName} · ${describe(option)}`
      : '';
  };
  protected readonly describe = describe;

  protected openRequest(): void {
    this.requestForm.reset();
    this.requestForm.controls.theirs.disable();
    this.chosenMine.set('');
    this.requestProblem.set(null);
    this.requestOpen.set(true);
  }

  protected chooseMine(id: string): void {
    this.requestForm.patchValue({ mine: id, theirs: '' });
    this.requestForm.controls.theirs.enable();
    this.chosenMine.set(id);
  }

  protected async request(): Promise<void> {
    this.requestForm.markAllAsTouched();
    if (this.requestForm.invalid || this.busy()) {
      return;
    }

    const { mine, theirs, message } = this.requestForm.getRawValue();
    this.busy.set(true);
    this.requestProblem.set(null);
    try {
      const swap = await this.api.invoke(requestSwap, {
        body: { requesterEntryId: mine, targetEntryId: theirs, message: message.trim() || null },
      });
      this.requestOpen.set(false);
      toast.success('Swap requested', {
        description: `${swap.target.employee.firstName} needs to accept it first.`,
      });
      this.swaps.reload();
    } catch (error) {
      this.requestProblem.set(toProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

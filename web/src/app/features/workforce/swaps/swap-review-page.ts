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
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../../api/api';
import { approveSwap } from '../../../api/fn/workforce/approve-swap';
import { listSwapsForReview } from '../../../api/fn/workforce/list-swaps-for-review';
import { rejectSwap } from '../../../api/fn/workforce/reject-swap';
import { type SwapResponse } from '../../../api/models/swap-response';
import { toProblem } from '../../../core/http/problem';
import { SwapCard } from './swap-card';

/** Swaps both nurses agreed to, waiting for approval on the wards you approve for. Soonest shift first. */
@Component({
  selector: 'app-swap-review-page',
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
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl">
      <ui-page-header
        title="Swap approvals"
        description="Swaps both nurses agreed to. Approving exchanges the shifts on the roster."
      />

      @if (swaps.error()) {
        <div hlmAlert variant="destructive">
          <ng-icon name="lucideCircleAlert" aria-hidden="true" />
          <h2 hlmAlertTitle>Couldn't load swaps</h2>
          <p hlmAlertDescription>Please try again.</p>
          <div hlmAlertAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="swaps.reload()">
              Try again
            </button>
          </div>
        </div>
      } @else if (!swaps.hasValue()) {
        <div hlmSkeleton class="h-64 w-full rounded-lg" aria-busy="true"></div>
      } @else {
        @for (swap of pending(); track swap.id) {
          <app-swap-card class="mb-3 block" [swap]="swap">
            <button
              hlmBtn
              variant="outline"
              type="button"
              [disabled]="busy()"
              (click)="openReject(swap)"
            >
              Reject
            </button>
            <button hlmBtn type="button" [disabled]="busy()" (click)="approve(swap)">
              Approve
            </button>
          </app-swap-card>
        } @empty {
          <ui-empty-state
            icon="lucideInbox"
            title="Nothing to approve"
            description="Swaps appear here once the colleague has accepted."
          />
        }
      }
    </div>

    <hlm-dialog [state]="rejecting() ? 'open' : 'closed'" (closed)="rejecting.set(null)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-md">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>Reject this swap?</h2>
          <p hlmDialogDescription>Both nurses keep their shifts. They see your note.</p>
        </hlm-dialog-header>
        <form id="reject-form" [formGroup]="rejectForm" (ngSubmit)="reject()" novalidate>
          <ui-form-field label="Note" for="reject-note" hint="Optional, e.g. staffing on that day">
            <textarea hlmTextarea id="reject-note" formControlName="note" rows="2"></textarea>
          </ui-form-field>
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="rejecting.set(null)">
            Keep it
          </button>
          <button hlmBtn variant="destructive" type="submit" form="reject-form">Reject swap</button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class SwapReviewPage {
  private readonly api = inject(Api);

  protected readonly swaps = resource({
    loader: () => this.api.invoke(listSwapsForReview, { pageSize: 100 }),
  });
  protected readonly pending = computed(() =>
    this.swaps.hasValue() ? this.swaps.value().items : [],
  );
  protected readonly busy = signal(false);

  protected approve(swap: SwapResponse): Promise<void> {
    return this.run(
      () =>
        this.api.invoke(approveSwap, { id: swap.id, body: { note: null, version: swap.version } }),
      'Swap approved',
      `${swap.requester.employee.firstName} and ${swap.target.employee.firstName} have exchanged shifts on the roster.`,
    );
  }

  protected readonly rejecting = signal<SwapResponse | null>(null);
  protected readonly rejectForm = inject(FormBuilder).nonNullable.group({
    note: ['', Validators.maxLength(300)],
  });

  protected openReject(swap: SwapResponse): void {
    this.rejectForm.reset();
    this.rejecting.set(swap);
  }

  protected async reject(): Promise<void> {
    const swap = this.rejecting();
    if (!swap || this.rejectForm.invalid) {
      return;
    }
    this.rejecting.set(null);
    await this.run(
      () =>
        this.api.invoke(rejectSwap, {
          id: swap.id,
          body: { note: this.rejectForm.getRawValue().note.trim() || null, version: swap.version },
        }),
      'Swap rejected',
      'Both nurses keep their shifts.',
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
}

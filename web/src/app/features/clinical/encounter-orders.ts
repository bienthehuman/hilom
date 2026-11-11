import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { ConfirmAction } from '@hilom/ui/confirm-action';
import { EmptyState } from '@hilom/ui/empty-state';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideClipboardList, lucidePlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { discontinuePhysicianOrder } from '../../api/fn/clinical/discontinue-physician-order';
import { listPhysicianOrders } from '../../api/fn/clinical/list-physician-orders';
import { type PhysicianOrderResponse } from '../../api/models/physician-order-response';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { labelOf, ORDER_TYPES } from './encounter-labels';
import { WriteOrderDialog } from './write-order-dialog';

/**
 * The encounter's Orders tab: physician orders newest first. A discontinued order stays in the list,
 * with who stopped it, when, and why. Discontinuing asks for a reason.
 */
@Component({
  selector: 'app-encounter-orders',
  imports: [
    NgIcon,
    EmptyState,
    StatusPill,
    ConfirmAction,
    WriteOrderDialog,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideClipboardList, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    @if (canWrite()) {
      <div class="flex justify-end">
        <button hlmBtn type="button" (click)="dialogOpen.set(true)">
          <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />New order
        </button>
      </div>
    }

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h3 hlmAlertTitle>Couldn't load orders</h3>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="orders.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (list() === undefined) {
      <div class="flex flex-col gap-3" aria-busy="true">
        <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
        <div hlmSkeleton class="h-24 w-full rounded-lg"></div>
      </div>
    } @else if (list()!.length === 0) {
      <div class="rounded-lg border bg-card">
        <ui-empty-state
          icon="lucideClipboardList"
          title="No orders yet"
          [description]="
            canWrite()
              ? 'Write the first medication, laboratory, diet or nursing order.'
              : 'Medication, laboratory, diet and nursing orders appear here.'
          "
        />
      </div>
    } @else {
      <ol class="flex flex-col gap-3" aria-label="Orders, newest first">
        @for (o of list(); track o.id) {
          <li class="rounded-lg border bg-card p-4" [class.text-muted-foreground]="!isActive(o)">
            <article [attr.aria-labelledby]="'order-heading-' + o.id">
              <header class="mb-2 flex flex-wrap items-start justify-between gap-2">
                <div class="min-w-0">
                  <h3 [id]="'order-heading-' + o.id" class="font-semibold">
                    @if (o.medicationName) {
                      <span [class.line-through]="o.status === 'discontinued'">{{
                        o.medicationName
                      }}</span>
                    } @else {
                      {{ typeLabel(o.orderType) }} order
                    }
                  </h3>
                  <p class="text-xs text-muted-foreground">
                    <span data-numeric>{{ formatDateTime(o.orderedAt) }}</span> · Dr.
                    {{ o.orderedBy }}
                  </p>
                </div>
                <div class="flex flex-wrap items-center gap-1">
                  <ui-status-pill kind="order" [status]="o.status" />
                  @if (o.canDiscontinue) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      type="button"
                      [attr.aria-label]="
                        'Discontinue ' + (o.medicationName ?? typeLabel(o.orderType) + ' order')
                      "
                      [uiConfirmAction]="discontinue(o)"
                      confirmTitle="Discontinue this order?"
                      confirmDescription="It stays on the record, marked discontinued, with your reason. This can't be undone."
                      confirmLabel="Discontinue"
                      reasonHint="e.g. rash after the first dose"
                      requireReason
                      destructive
                    >
                      Discontinue
                    </button>
                  }
                </div>
              </header>
              @if (o.medicationName) {
                <dl class="mb-2 grid max-w-xl grid-cols-3 gap-x-4 text-sm">
                  <div>
                    <dt class="text-xs text-muted-foreground">Dose</dt>
                    <dd data-numeric>{{ o.dose }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Route</dt>
                    <dd>{{ o.route }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Frequency</dt>
                    <dd>{{ o.frequency }}</dd>
                  </div>
                </dl>
              }
              <p class="max-w-[72ch] whitespace-pre-line">{{ o.details }}</p>
              @if (o.discontinued; as stopped) {
                <p class="mt-3 border-t pt-2 text-sm">
                  <span class="font-medium">Discontinued</span>
                  <span data-numeric> {{ formatDateTime(stopped.at) }}</span>
                  @if (stopped.by) {
                    by {{ stopped.by }}
                  }
                  <span class="block">Reason: {{ stopped.reason }}</span>
                </p>
              }
            </article>
          </li>
        }
      </ol>
    }

    <app-write-order-dialog
      [encounterId]="encounterId()"
      [open]="dialogOpen()"
      (closed)="dialogOpen.set(false)"
      (saved)="onSaved()"
    />
  `,
})
export class EncounterOrders {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  /** Whether the caller may write a new order (the encounter's `canWriteOrders`). */
  readonly canWrite = input(false);

  protected readonly formatDateTime = formatDateTime;
  protected readonly dialogOpen = signal(false);

  protected readonly orders = resource({
    params: () => this.encounterId(),
    loader: ({ params }) => this.api.invoke(listPhysicianOrders, { encounterId: params }),
  });

  /** Kept while reloading after a change, so the list doesn't flash back to skeletons. */
  protected readonly list = linkedSignal<
    PhysicianOrderResponse[] | undefined,
    PhysicianOrderResponse[] | undefined
  >({
    source: () => (this.orders.hasValue() ? this.orders.value() : undefined),
    computation: (next, previous) => next ?? (this.orders.error() ? undefined : previous?.value),
  });

  protected readonly loadError = computed(() =>
    this.orders.error() ? (toProblem(this.orders.error()).detail ?? "Couldn't load orders.") : null,
  );

  protected typeLabel(type: string): string {
    return labelOf(ORDER_TYPES, type);
  }

  protected isActive(order: PhysicianOrderResponse): boolean {
    return order.status === 'active';
  }

  protected onSaved(): void {
    this.dialogOpen.set(false);
    this.orders.reload();
  }

  protected discontinue(order: PhysicianOrderResponse): (reason: string) => Promise<void> {
    return async (reason) => {
      try {
        await this.api.invoke(discontinuePhysicianOrder, {
          encounterId: this.encounterId(),
          id: order.id,
          body: { reason, version: order.version },
        });
      } catch (error) {
        // Someone else already stopped it: show what's current.
        if (toProblem(error).status === 409) {
          this.orders.reload();
        }
        throw error;
      }
      toast.success('Order discontinued');
      this.orders.reload();
    };
  }
}

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HlmCardImports } from '@hilom/ui/card';
import { ShiftChip, formatShiftHours } from '@hilom/ui/shift-chip';
import { StatusPill } from '@hilom/ui/status';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeftRight } from '@ng-icons/lucide';

import { type SwapResponse } from '../../../api/models/swap-response';
import { type SwapSide } from '../../../api/models/swap-side';
import { formatDateTime, formatLongDay } from '../../../core/format/date-time';

/** Both sides of a swap, its status and the notes along the way. Actions go in the projected slot. */
@Component({
  selector: 'app-swap-card',
  imports: [HlmCardImports, NgIcon, ShiftChip, StatusPill],
  providers: [provideIcons({ lucideArrowLeftRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article hlmCard class="gap-3" [attr.aria-label]="label()">
      <div hlmCardHeader class="flex flex-wrap items-center justify-between gap-2">
        <span class="text-sm text-muted-foreground">
          {{ swap().ward.code }} · {{ swap().ward.name }} · asked {{ asked() }}
        </span>
        <ui-status-pill kind="swap" [status]="swap().status" />
      </div>
      <div hlmCardContent class="flex flex-col gap-3">
        <div class="grid items-center gap-3 sm:grid-cols-[1fr_auto_1fr]">
          @for (side of sides(); track side.who; let first = $first) {
            @if (!first) {
              <ng-icon
                name="lucideArrowLeftRight"
                aria-hidden="true"
                class="hidden justify-self-center text-muted-foreground sm:block"
              />
            }
            <div class="flex flex-col gap-1 rounded-md border p-3">
              <span class="text-sm font-medium">{{ side.who }}</span>
              <span class="text-xs text-muted-foreground">gives up</span>
              <span class="flex flex-wrap items-center gap-2 text-sm">
                <ui-shift-chip
                  [code]="side.side.shift.code"
                  [color]="side.side.shift.color"
                  [name]="side.side.shift.name"
                />
                <span data-numeric>{{ day(side.side) }} · {{ hours(side.side) }}</span>
              </span>
            </div>
          }
        </div>
        @if (swap().message) {
          <p class="text-sm">
            <span class="text-muted-foreground">Message:</span> {{ swap().message }}
          </p>
        }
        @if (swap().targetResponse) {
          <p class="text-sm">
            <span class="text-muted-foreground"
              >{{ swap().target.employee.firstName }}'s reply:</span
            >
            {{ swap().targetResponse }}
          </p>
        }
        @if (swap().reviewedAt) {
          <p class="text-sm">
            <span class="text-muted-foreground">
              {{ swap().status === 'approved' ? 'Approved' : 'Rejected' }} by
              {{ swap().reviewedBy ?? 'a reviewer' }}{{ swap().reviewNote ? ':' : '' }}
            </span>
            {{ swap().reviewNote }}
          </p>
        }
        <div class="flex flex-wrap justify-end gap-2 empty:hidden">
          <ng-content />
        </div>
      </div>
    </article>
  `,
})
export class SwapCard {
  readonly swap = input.required<SwapResponse>();

  protected readonly sides = computed(() => {
    const s = this.swap();
    const name = (side: SwapSide, you: boolean) =>
      you ? 'You' : `${side.employee.firstName} ${side.employee.lastName}`;
    return [
      { who: name(s.requester, s.youAre === 'requester'), side: s.requester },
      { who: name(s.target, s.youAre === 'target'), side: s.target },
    ];
  });

  protected readonly label = computed(() => {
    const [a, b] = this.sides();
    return `Swap: ${a!.who} ${this.day(a!.side)} ${a!.side.shift.code} for ${b!.who} ${this.day(b!.side)} ${b!.side.shift.code}`;
  });

  protected readonly asked = computed(() => formatDateTime(this.swap().createdAt));
  protected readonly day = (side: SwapSide) => formatLongDay(side.dutyDate);
  protected readonly hours = (side: SwapSide) =>
    formatShiftHours(side.shift.startsAt, side.shift.endsAt, side.shift.crossesMidnight);
}

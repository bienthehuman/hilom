import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
  signal,
} from '@angular/core';

/** Chart colors from the design tokens. `chart-1` + `chart-4` is the validated pair for two series. */
export type ChartColor = 'chart-1' | 'chart-2' | 'chart-3' | 'chart-4' | 'chart-5';

export interface TrendPoint {
  /** ISO timestamp. */
  at: string;
  value: number;
  /** Shown in the tooltip next to the value, e.g. "high". Points with a note get a status ring. */
  note?: string | null;
  /** `warning` or `danger` ring around the point; none when in range. */
  tone?: 'warning' | 'danger' | null;
}

export interface TrendSeries {
  name: string;
  color: ChartColor;
  points: readonly TrendPoint[];
}

export interface TrendBand {
  low: number;
  high: number;
  label: string;
}

const HEIGHT = 200;
const MARGIN = { top: 12, right: 16, bottom: 28, left: 44 };

/** Rounded tick step for a span, e.g. 7.3 → 2, 140 → 50. */
function niceStep(span: number, count: number): number {
  const raw = span / Math.max(count, 1);
  const power = 10 ** Math.floor(Math.log10(raw));
  const fraction = raw / power;
  const nice =
    fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
  return nice * power;
}

/**
 * A small line chart of readings over time (docs/DESIGN_SYSTEM.md §7), drawn in SVG with the design
 * tokens, so it follows light and dark mode. One axis only; a shaded band for the normal range;
 * points outside it get a status ring. Hover, or focus and use the arrow keys, to read every series
 * at a time. Always pair it with a table of the same readings: the chart helps, the table is the record.
 *
 * ```html
 * <ui-trend-chart label="Heart rate" unit="bpm" [series]="series" [band]="band" [formatTime]="fmt" />
 * ```
 */
@Component({
  selector: 'ui-trend-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block' },
  template: `
    @if (legend().length > 0) {
      <ul class="mb-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
        @for (item of legend(); track item.name) {
          <li class="flex items-center gap-1.5">
            @if (item.kind === 'line') {
              <span
                class="inline-block h-0.5 w-4 rounded-full"
                [style.background]="'var(--' + item.color + ')'"
                aria-hidden="true"
              ></span>
            } @else {
              <span
                class="inline-block size-3 rounded-sm border border-border bg-muted"
                aria-hidden="true"
              ></span>
            }
            {{ item.name }}
          </li>
        }
      </ul>
    }
    <svg
      class="block w-full overflow-visible rounded-md outline-none focus-visible:ring-2 focus-visible:ring-ring"
      [attr.viewBox]="'0 0 ' + width() + ' ' + height"
      [attr.height]="height"
      role="img"
      tabindex="0"
      [attr.aria-label]="ariaLabel()"
      (pointermove)="onPointer($event)"
      (pointerleave)="active.set(null)"
      (focus)="active.set(times().length - 1)"
      (blur)="active.set(null)"
      (keydown)="onKey($event)"
    >
      @for (tick of yTicks(); track tick) {
        <line
          [attr.x1]="margin.left"
          [attr.x2]="width() - margin.right"
          [attr.y1]="y(tick)"
          [attr.y2]="y(tick)"
          stroke="var(--border)"
          stroke-width="1"
        />
        <text
          [attr.x]="margin.left - 8"
          [attr.y]="y(tick)"
          text-anchor="end"
          dominant-baseline="middle"
          class="fill-muted-foreground text-xs tabular-nums"
        >
          {{ tick }}
        </text>
      }
      @if (band(); as b) {
        <rect
          [attr.x]="margin.left"
          [attr.width]="plotWidth()"
          [attr.y]="y(b.high)"
          [attr.height]="Math.max(0, y(b.low) - y(b.high))"
          fill="var(--muted)"
        />
      }
      @for (label of xLabels(); track label.at) {
        <text
          [attr.x]="x(label.at)"
          [attr.y]="height - 8"
          [attr.text-anchor]="label.anchor"
          class="fill-muted-foreground text-xs tabular-nums"
        >
          {{ formatTime()(label.at) }}
        </text>
      }
      @if (active() !== null) {
        <line
          [attr.x1]="x(times()[active()!]!)"
          [attr.x2]="x(times()[active()!]!)"
          [attr.y1]="margin.top"
          [attr.y2]="height - margin.bottom"
          stroke="var(--muted-foreground)"
          stroke-width="1"
        />
      }
      @for (s of series(); track s.name) {
        @if (s.points.length > 1) {
          <path
            [attr.d]="path(s)"
            fill="none"
            [attr.stroke]="'var(--' + s.color + ')'"
            stroke-width="2"
            stroke-linejoin="round"
            stroke-linecap="round"
          />
        }
        @for (p of s.points; track p.at) {
          <circle
            [attr.cx]="x(p.at)"
            [attr.cy]="y(p.value)"
            r="4"
            [attr.fill]="'var(--' + s.color + ')'"
            [attr.stroke]="
              p.tone === 'danger'
                ? 'var(--destructive)'
                : p.tone === 'warning'
                  ? 'var(--warning)'
                  : 'var(--card)'
            "
            stroke-width="2"
          />
        }
      }
    </svg>
    @if (readout(); as r) {
      <div
        class="pointer-events-none absolute z-10 min-w-36 rounded-md border bg-popover px-3 py-2 text-xs text-popover-foreground shadow-md"
        [style.left.px]="r.left"
        [style.top.px]="r.top"
        [style.transform]="r.flip ? 'translateX(-100%) translateX(-12px)' : 'translateX(12px)'"
      >
        <p class="mb-1 text-muted-foreground tabular-nums">{{ formatTime()(r.at) }}</p>
        @for (row of r.rows; track row.name) {
          <p class="flex items-center gap-2">
            <span
              class="inline-block h-0.5 w-3 rounded-full"
              [style.background]="'var(--' + row.color + ')'"
              aria-hidden="true"
            ></span>
            <span class="font-semibold tabular-nums">{{ row.value }} {{ unit() }}</span>
            <span class="text-muted-foreground">{{ row.name }}</span>
            @if (row.note) {
              <span
                [class.text-destructive]="row.tone === 'danger'"
                [class.text-warning]="row.tone === 'warning'"
                >{{ row.note }}</span
              >
            }
          </p>
        }
      </div>
    }
    <p class="sr-only" aria-live="polite">{{ announcement() }}</p>
  `,
})
export class TrendChart {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** What's plotted, e.g. "Heart rate". Also the chart's accessible name. */
  readonly label = input.required<string>();
  readonly unit = input('');
  readonly series = input.required<readonly TrendSeries[]>();
  readonly band = input<TrendBand | null>(null);
  /** Formats a timestamp for the axis and tooltip, in hospital time. */
  readonly formatTime = input.required<(iso: string) => string>();

  protected readonly height = HEIGHT;
  protected readonly margin = MARGIN;
  protected readonly Math = Math;
  protected readonly width = signal(600);
  /** Index into `times()` under the pointer or keyboard focus. */
  protected readonly active = signal<number | null>(null);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const element = this.host.nativeElement;
      const observer = new ResizeObserver(([entry]) => {
        const next = Math.round(entry?.contentRect.width ?? 0);
        if (next > 0) {
          this.width.set(next);
        }
      });
      observer.observe(element);
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  protected readonly plotWidth = computed(() => this.width() - MARGIN.left - MARGIN.right);

  /** Every distinct reading time across the series, oldest first. */
  protected readonly times = computed(() =>
    [...new Set(this.series().flatMap((s) => s.points.map((p) => p.at)))].sort(
      (a, b) => Date.parse(a) - Date.parse(b),
    ),
  );

  private readonly xDomain = computed(() => {
    const times = this.times().map((t) => Date.parse(t));
    return times.length ? [Math.min(...times), Math.max(...times)] : [0, 1];
  });

  private readonly yDomain = computed(() => {
    const values = this.series().flatMap((s) => s.points.map((p) => p.value));
    const band = this.band();
    if (band) {
      values.push(band.low, band.high);
    }
    if (values.length === 0) {
      return { min: 0, max: 1, step: 1 };
    }
    let min = Math.min(...values);
    let max = Math.max(...values);
    if (min === max) {
      min -= 1;
      max += 1;
    }
    const step = niceStep(max - min, 4);
    return { min: Math.floor(min / step) * step, max: Math.ceil(max / step) * step, step };
  });

  protected readonly yTicks = computed(() => {
    const { min, max, step } = this.yDomain();
    const ticks: number[] = [];
    for (let v = min; v <= max + step / 2; v += step) {
      ticks.push(Math.round(v * 100) / 100);
    }
    return ticks;
  });

  /** First and last time, plus the middle when there's room. */
  protected readonly xLabels = computed(() => {
    const times = this.times();
    if (times.length === 0) {
      return [];
    }
    if (times.length === 1) {
      return [{ at: times[0]!, anchor: 'middle' }];
    }
    const labels = [
      { at: times[0]!, anchor: 'start' },
      { at: times[times.length - 1]!, anchor: 'end' },
    ];
    if (times.length > 2 && this.plotWidth() > 420) {
      labels.splice(1, 0, { at: times[Math.floor(times.length / 2)]!, anchor: 'middle' });
    }
    return labels;
  });

  protected x(at: string): number {
    const [min, max] = this.xDomain() as [number, number];
    if (max === min) {
      return MARGIN.left + this.plotWidth() / 2;
    }
    return MARGIN.left + ((Date.parse(at) - min) / (max - min)) * this.plotWidth();
  }

  protected y(value: number): number {
    const { min, max } = this.yDomain();
    const plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom;
    return MARGIN.top + (1 - (value - min) / (max - min)) * plotHeight;
  }

  protected path(s: TrendSeries): string {
    return [...s.points]
      .sort((a, b) => Date.parse(a.at) - Date.parse(b.at))
      .map(
        (p, i) => `${i === 0 ? 'M' : 'L'}${this.x(p.at).toFixed(1)},${this.y(p.value).toFixed(1)}`,
      )
      .join(' ');
  }

  protected readonly legend = computed(() => {
    const items: { name: string; kind: 'line' | 'band'; color?: ChartColor }[] = [];
    if (this.series().length > 1) {
      items.push(
        ...this.series().map((s) => ({ name: s.name, kind: 'line' as const, color: s.color })),
      );
    }
    const band = this.band();
    if (band) {
      items.push({ name: band.label, kind: 'band' });
    }
    return items;
  });

  protected readonly readout = computed(() => {
    const index = this.active();
    const at = index === null ? undefined : this.times()[index];
    if (!at) {
      return null;
    }
    const rows = this.series().flatMap((s) => {
      const p = s.points.find((point) => point.at === at);
      return p
        ? [{ name: s.name, color: s.color, value: p.value, note: p.note, tone: p.tone }]
        : [];
    });
    const left = this.x(at);
    // Keep the tooltip off the readings it describes: below them when they sit high in the plot.
    const highest = Math.min(...rows.map((row) => this.y(row.value)));
    const offset = this.legend().length > 0 ? 28 : 0;
    const top = offset + (highest < HEIGHT / 2 ? HEIGHT - MARGIN.bottom - 64 : 4);
    return { at, rows, left, top, flip: left > this.width() / 2 };
  });

  protected readonly announcement = computed(() => {
    const r = this.readout();
    if (!r) {
      return '';
    }
    const values = r.rows
      .map((row) => `${row.name} ${row.value} ${this.unit()}${row.note ? `, ${row.note}` : ''}`)
      .join('; ');
    return `${this.formatTime()(r.at)}: ${values}`;
  });

  protected readonly ariaLabel = computed(() => {
    const count = this.times().length;
    return `${this.label()} trend, ${count} ${count === 1 ? 'reading' : 'readings'}. Use the arrow keys to read each one.`;
  });

  protected onPointer(event: PointerEvent): void {
    const times = this.times();
    if (times.length === 0) {
      return;
    }
    const svg = event.currentTarget as SVGSVGElement;
    const box = svg.getBoundingClientRect();
    const px = ((event.clientX - box.left) / box.width) * this.width();
    let nearest = 0;
    let best = Infinity;
    times.forEach((t, i) => {
      const distance = Math.abs(this.x(t) - px);
      if (distance < best) {
        best = distance;
        nearest = i;
      }
    });
    this.active.set(nearest);
  }

  protected onKey(event: KeyboardEvent): void {
    const last = this.times().length - 1;
    if (last < 0) {
      return;
    }
    const current = this.active() ?? last;
    const next =
      event.key === 'ArrowLeft'
        ? Math.max(0, current - 1)
        : event.key === 'ArrowRight'
          ? Math.min(last, current + 1)
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : null;
    if (next !== null) {
      event.preventDefault();
      this.active.set(next);
    } else if (event.key === 'Escape') {
      this.active.set(null);
    }
  }
}

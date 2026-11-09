import { Directive, input } from '@angular/core';
import { classes } from '@hilom/ui/utils';
import { cva, type VariantProps } from 'class-variance-authority';

const alertVariants = cva(
  "grid gap-0.5 rounded-lg border px-4 py-3 text-start text-sm has-data-[slot=alert-action]:relative has-data-[slot=alert-action]:pr-18 has-[>ng-icon]:grid-cols-[auto_1fr] has-[>ng-icon]:gap-x-2.5 *:[ng-icon]:row-span-2 *:[ng-icon]:translate-y-0.5 *:[ng-icon]:text-current *:[ng-icon:not([class*='text-'])]:text-[length:--spacing(4)] group/alert relative w-full",
  {
    variants: {
      // Hilom: tinted "soft" backgrounds per meaning color (docs/DESIGN_SYSTEM.md §2-3).
      // Titles and icons carry the color; body text stays foreground for readability.
      variant: {
        default: 'bg-card text-card-foreground',
        destructive:
          'border-destructive/30 bg-destructive-soft text-foreground *:data-[slot=alert-title]:text-destructive *:[ng-icon]:text-destructive',
        warning:
          'border-warning/30 bg-warning-soft text-foreground *:data-[slot=alert-title]:text-warning *:[ng-icon]:text-warning',
        success:
          'border-success/30 bg-success-soft text-foreground *:data-[slot=alert-title]:text-success *:[ng-icon]:text-success',
        info: 'border-info/30 bg-info-soft text-foreground *:data-[slot=alert-title]:text-info *:[ng-icon]:text-info',
      },
    },
    defaultVariants: {
      variant: 'default',
    },
  },
);

export type AlertVariants = VariantProps<typeof alertVariants>;

@Directive({
  selector: 'hlm-alert,[hlmAlert]',
  exportAs: 'hlmAlert',
  host: {
    'data-slot': 'alert',
    role: 'alert',
  },
})
export class HlmAlert {
  public readonly variant = input<AlertVariants['variant']>('default');

  constructor() {
    classes(() => alertVariants({ variant: this.variant() }));
  }
}

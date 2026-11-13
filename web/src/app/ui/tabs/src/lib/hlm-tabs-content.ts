import { Directive, input } from '@angular/core';
import { BrnTabsContent } from '@spartan-ng/brain/tabs';
import { classes } from '@hilom/ui/utils';

@Directive({
  selector: '[hlmTabsContent]',
  exportAs: 'hlmTabsContent',
  hostDirectives: [{ directive: BrnTabsContent, inputs: ['brnTabsContent: hlmTabsContent'] }],
  host: {
    'data-slot': 'tabs-content',
  },
})
export class HlmTabsContent {
  public readonly contentFor = input.required<string>({ alias: 'hlmTabsContent' });

  constructor() {
    classes(
      () =>
        'flex-1 rounded-md text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50',
    );
  }
}

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusPill } from '@hilom/ui/status';

import { type EncounterSummaryResponse } from '../../api/models/encounter-summary-response';
import { formatPatientName } from '../patients/patient-labels';

/** Patient name linking to the encounter, with the MRN underneath. */
@Component({
  selector: 'app-encounter-patient-cell',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col">
      <a
        class="font-medium text-foreground underline-offset-4 hover:underline focus-visible:underline"
        [routerLink]="['/clinical/encounters', encounter().id]"
        >{{ name() }}</a
      >
      <span class="text-xs text-muted-foreground" data-numeric>{{ encounter().patient.mrn }}</span>
    </div>
  `,
})
export class EncounterPatientCell {
  readonly encounter = input.required<EncounterSummaryResponse>();
  protected readonly name = computed(() => formatPatientName(this.encounter().patient));
}

@Component({
  selector: 'app-encounter-status-cell',
  imports: [StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ui-status-pill kind="encounter" [status]="status()" />`,
})
export class EncounterStatusCell {
  readonly status = input.required<string>();
}

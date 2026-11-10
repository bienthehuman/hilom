import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { type PatientSummaryResponse } from '../../api/models/patient-summary-response';
import { formatPatientName } from './patient-labels';

/** Name linking to the record, with the MRN underneath. */
@Component({
  selector: 'app-patient-name-cell',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col">
      <a
        class="font-medium text-foreground underline-offset-4 hover:underline focus-visible:underline"
        [routerLink]="['/patients', patient().id]"
        >{{ name() }}</a
      >
      <span class="text-xs text-muted-foreground" data-numeric>{{ patient().mrn }}</span>
    </div>
  `,
})
export class PatientNameCell {
  readonly patient = input.required<PatientSummaryResponse>();
  protected readonly name = computed(() => formatPatientName(this.patient()));
}

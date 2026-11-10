import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCircleAlert, lucideIdCard } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { requestLink } from '../../api/fn/patients/request-link';
import { type LinkRelationship } from '../../api/models/link-relationship';
import { hospitalToday } from '../../core/format/date-time';
import { labelOf } from '../../core/format/options';
import { type Problem, toProblem } from '../../core/http/problem';
import { RELATIONSHIPS_FOR_REQUESTER } from '../patients/link-labels';

/**
 * Ask the hospital to link a patient to this account. The details are only what the requester knows; the
 * front desk finds the record and checks ID before verifying (docs/adr/0003-patient-account-links.md).
 */
@Component({
  selector: 'app-portal-link-request-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    FormField,
    HlmAlertImports,
    HlmButtonImports,
    HlmInputImports,
    HlmSelectImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideArrowLeft, lucideCircleAlert, lucideIdCard })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" class="-ms-3 mb-2" routerLink="/portal/patients">
      <ng-icon name="lucideArrowLeft" aria-hidden="true" data-icon="inline-start" />Patients
    </a>
    <h1 class="mb-1 text-2xl font-semibold tracking-tight">Link a patient</h1>
    <p class="mb-6 text-muted-foreground">
      Tell us who the patient is. We'll match them to their hospital record.
    </p>

    <div hlmAlert variant="info" class="mb-6">
      <ng-icon name="lucideIdCard" aria-hidden="true" />
      <h2 hlmAlertTitle>Bring a valid ID</h2>
      <p hlmAlertDescription>
        For the patient's privacy, the front desk checks your ID before linking. Until then you
        can't see their details.
      </p>
    </div>

    @if (problem(); as p) {
      <div hlmAlert variant="destructive" class="mb-6" role="alert">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h2 hlmAlertTitle>Couldn't send your request</h2>
        <p hlmAlertDescription>{{ p.detail ?? 'Please try again.' }}</p>
      </div>
    }

    <form class="flex flex-col gap-5" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <ui-form-field label="You are the patient's" for="link-relationship" required>
        <hlm-select formControlName="relationship" [itemToString]="relationshipLabel">
          <hlm-select-trigger buttonId="link-relationship" class="w-full">
            <hlm-select-value placeholder="Choose" />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (option of relationships; track option.value) {
              <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </ui-form-field>
      <ui-form-field label="Patient's full name" for="link-name" required>
        <input hlmInput id="link-name" formControlName="patientName" autocomplete="off" />
      </ui-form-field>
      <ui-form-field label="Patient's date of birth" for="link-birth-date" required>
        <input
          hlmInput
          id="link-birth-date"
          type="date"
          formControlName="patientBirthDate"
          [max]="today"
        />
      </ui-form-field>
      <ui-form-field
        label="Medical record number"
        for="link-mrn"
        hint="On the patient's hospital card, e.g. MRN-000123. Leave empty if you don't have it."
      >
        <input
          hlmInput
          id="link-mrn"
          formControlName="patientMrn"
          autocomplete="off"
          autocapitalize="characters"
          spellcheck="false"
        />
      </ui-form-field>
      <ui-form-field label="Anything else the hospital should know" for="link-note">
        <textarea hlmTextarea id="link-note" rows="3" formControlName="note"></textarea>
      </ui-form-field>

      <button hlmBtn type="submit" class="w-full" [disabled]="submitting()">
        {{ submitting() ? 'Sending…' : 'Send request' }}
      </button>
    </form>
  `,
})
export class PortalLinkRequestPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly today = hospitalToday();
  protected readonly relationships = RELATIONSHIPS_FOR_REQUESTER;
  protected readonly relationshipLabel = (value: string) =>
    labelOf(RELATIONSHIPS_FOR_REQUESTER, value);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    relationship: this.fb.control<LinkRelationship | null>(null, Validators.required),
    patientName: ['', [Validators.required, Validators.maxLength(200)]],
    patientBirthDate: ['', Validators.required],
    patientMrn: ['', Validators.maxLength(20)],
    note: ['', Validators.maxLength(500)],
  });

  protected readonly submitting = signal(false);
  protected readonly problem = signal<Problem | null>(null);

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }

    const value = this.form.getRawValue();
    const optional = (text: string) => (text.trim() === '' ? null : text.trim());
    this.submitting.set(true);
    this.problem.set(null);
    try {
      await this.api.invoke(requestLink, {
        body: {
          relationship: value.relationship,
          patientName: value.patientName.trim(),
          patientBirthDate: value.patientBirthDate,
          patientMrn: optional(value.patientMrn),
          note: optional(value.note),
        },
      });
      toast.success('Request sent', {
        description: 'Bring a valid ID to the front desk so they can confirm who you are.',
      });
      await this.router.navigateByUrl('/portal/patients');
    } catch (error) {
      const problem = toProblem(error);
      let placed = false;
      for (const [field, messages] of Object.entries(problem.errors ?? {})) {
        const control = this.form.get(field);
        if (control && messages[0]) {
          control.setErrors({ server: messages[0] });
          placed = true;
        }
      }
      if (!placed) {
        this.problem.set(problem);
      }
    } finally {
      this.submitting.set(false);
    }
  }
}

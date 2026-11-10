import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HlmAlertImports } from '@hilom/ui/alert';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmCheckboxImports } from '@hilom/ui/checkbox';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { EmptyState } from '@hilom/ui/empty-state';
import { FormField } from '@hilom/ui/form-field';
import { HlmInputImports } from '@hilom/ui/input';
import { HlmLabelImports } from '@hilom/ui/label';
import { PageHeader } from '@hilom/ui/page-header';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmSkeletonImports } from '@hilom/ui/skeleton';
import { StatusPill } from '@hilom/ui/status';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideInbox, lucideSearch, lucideSearchX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { listLinks } from '../../api/fn/patients/list-links';
import { listPatients } from '../../api/fn/patients/list-patients';
import { rejectLink } from '../../api/fn/patients/reject-link';
import { verifyLink } from '../../api/fn/patients/verify-link';
import { type LinkRelationship } from '../../api/models/link-relationship';
import { type LinkResponse } from '../../api/models/link-response';
import { type LinkStatusFilter } from '../../api/models/link-status-filter';
import { type PatientSummaryResponse } from '../../api/models/patient-summary-response';
import { formatDate, formatDateTime } from '../../core/format/date-time';
import { labelOf } from '../../core/format/options';
import { type Problem, toProblem } from '../../core/http/problem';
import { LinkAccessFields } from './link-access-fields';
import { RELATIONSHIPS, RELATIONSHIP_TO, defaultLinkAccess, describeAccess } from './link-labels';
import { formatPatientName } from './patient-labels';

const PAGE_SIZE = 20;

/**
 * Front desk: portal accounts asking to be linked to patients. Reviewing a request means finding the
 * patient's record, checking the requester's ID, and choosing what the account may do.
 */
@Component({
  selector: 'app-link-requests-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    NgIcon,
    PageHeader,
    EmptyState,
    FormField,
    StatusPill,
    LinkAccessFields,
    HlmAlertImports,
    HlmButtonImports,
    HlmCheckboxImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmSkeletonImports,
    HlmTextareaImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideInbox, lucideSearch, lucideSearchX })],
  templateUrl: './link-requests-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LinkRequestsPage {
  private readonly api = inject(Api);

  // ---- List ----
  protected readonly status = signal<LinkStatusFilter>('pending');
  protected readonly page = signal(1);
  protected readonly links = resource({
    params: () => ({ status: this.status(), page: this.page() }),
    loader: ({ params }) =>
      this.api.invoke(listLinks, { status: params.status, page: params.page, pageSize: PAGE_SIZE }),
  });
  protected readonly items = computed(() =>
    this.links.hasValue() ? this.links.value().items : [],
  );
  protected readonly total = computed(() => (this.links.hasValue() ? this.links.value().total : 0));
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  protected readonly loadError = computed(() =>
    this.links.error() ? (toProblem(this.links.error()).detail ?? "Couldn't load requests.") : null,
  );

  protected readonly statusOptions: readonly { value: LinkStatusFilter; label: string }[] = [
    { value: 'pending', label: 'Waiting for review' },
    { value: 'active', label: 'Active' },
    { value: 'rejected', label: 'Rejected' },
    { value: 'revoked', label: 'Revoked' },
  ];
  protected readonly statusLabel = (value: string | null) =>
    this.statusOptions.find((o) => o.value === value)?.label ?? value ?? '';

  protected setStatus(value: LinkStatusFilter): void {
    this.status.set(value);
    this.page.set(1);
  }

  protected readonly relationshipLabel = (value: string) => labelOf(RELATIONSHIPS, value);
  protected readonly relationships = RELATIONSHIPS;
  protected readonly relationTo = RELATIONSHIP_TO;
  protected readonly formatDate = formatDate;
  protected readonly formatDateTime = formatDateTime;
  protected readonly describeAccess = describeAccess;
  protected readonly patientName = formatPatientName;

  // ---- Review ----
  protected readonly reviewing = signal<LinkResponse | null>(null);
  protected readonly mode = signal<'verify' | 'reject'>('verify');
  protected readonly query = signal('');
  protected readonly sameBirthDate = signal(true);
  protected readonly chosenPatientId = signal<string | null>(null);
  protected readonly relationship = new FormControl<LinkRelationship>('self', {
    nonNullable: true,
  });
  protected readonly canBook = signal(true);
  protected readonly canViewRecords = signal(false);
  protected readonly note = new FormControl('', {
    nonNullable: true,
    validators: Validators.maxLength(500),
  });
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly patientError = signal<string | null>(null);
  protected readonly rejectReason = new FormControl('', {
    nonNullable: true,
    validators: Validators.maxLength(500),
  });

  constructor() {
    // A new relationship suggests its usual access; staff can still change it.
    this.relationship.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((relationship) => this.applyDefaultAccess(relationship));
  }

  protected readonly candidates = resource({
    params: () => {
      const link = this.reviewing();
      if (!link) {
        return undefined;
      }
      return {
        q: this.query().trim(),
        birthDate: this.sameBirthDate() ? link.requestedBirthDate : undefined,
      };
    },
    loader: ({ params }) =>
      this.api.invoke(listPatients, {
        q: params.q || undefined,
        birthDate: params.birthDate,
        pageSize: 8,
        sort: 'name',
      }),
  });
  /** Kept while searching again, so the list doesn't flash. */
  protected readonly candidateList = linkedSignal<
    PatientSummaryResponse[] | undefined,
    PatientSummaryResponse[]
  >({
    source: () => (this.candidates.hasValue() ? this.candidates.value().items : undefined),
    computation: (next, previous) => next ?? previous?.value ?? [],
  });

  protected openReview(link: LinkResponse): void {
    this.reviewing.set(link);
    this.mode.set('verify');
    // The MRN is the surest match; otherwise the last word of the name, with the date of birth.
    this.query.set(link.requestedMrn ?? link.requestedPatientName.trim().split(/\s+/).at(-1) ?? '');
    this.sameBirthDate.set(true);
    this.chosenPatientId.set(null);
    this.relationship.setValue(link.relationship);
    this.applyDefaultAccess(link.relationship);
    this.note.reset('');
    this.rejectReason.reset('');
    this.problem.set(null);
    this.patientError.set(null);
  }

  private applyDefaultAccess(relationship: LinkRelationship): void {
    const access = defaultLinkAccess(relationship);
    this.canBook.set(access.canBook);
    this.canViewRecords.set(access.canViewRecords);
  }

  protected close(): void {
    this.reviewing.set(null);
  }

  protected async verify(): Promise<void> {
    const link = this.reviewing();
    const patientId = this.chosenPatientId();
    if (!link || this.saving()) {
      return;
    }
    if (!patientId) {
      this.patientError.set('Choose the patient record that matches.');
      return;
    }

    await this.act(
      () =>
        this.api.invoke(verifyLink, {
          id: link.id,
          body: {
            patientId,
            relationship: this.relationship.value,
            canBook: this.canBook(),
            canViewRecords: this.canViewRecords(),
            note: this.note.value.trim() || null,
            version: link.version,
          },
        }),
      'Link verified',
    );
  }

  protected async reject(): Promise<void> {
    const link = this.reviewing();
    if (!link || this.saving()) {
      return;
    }
    await this.act(
      () =>
        this.api.invoke(rejectLink, {
          id: link.id,
          body: { reason: this.rejectReason.value.trim() || null, version: link.version },
        }),
      'Request rejected',
    );
  }

  private async act(call: () => Promise<LinkResponse>, message: string): Promise<void> {
    this.saving.set(true);
    this.problem.set(null);
    try {
      const saved = await call();
      toast.success(message, {
        description: `${saved.requester.displayName} · ${saved.patient ? formatPatientName(saved.patient) : saved.requestedPatientName}`,
      });
      this.reviewing.set(null);
      this.links.reload();
    } catch (error) {
      const problem = toProblem(error);
      if (problem.errors?.['patientId']?.[0]) {
        this.patientError.set(problem.errors['patientId'][0]);
      } else {
        if (problem.code === 'concurrency_conflict' || problem.code === 'link_not_pending') {
          this.links.reload();
        }
        this.problem.set(problem);
      }
    } finally {
      this.saving.set(false);
    }
  }
}

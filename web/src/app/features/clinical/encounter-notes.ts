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
import { VoidedPill } from '@hilom/ui/voided';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideFileText, lucidePlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { listClinicalNotes } from '../../api/fn/clinical/list-clinical-notes';
import { voidClinicalNote } from '../../api/fn/clinical/void-clinical-note';
import { type ClinicalNoteResponse } from '../../api/models/clinical-note-response';
import { type VoidedResponse } from '../../api/models/voided-response';
import { formatDateTime } from '../../core/format/date-time';
import { toProblem } from '../../core/http/problem';
import { ALL_NOTE_TYPES, labelOf } from './encounter-labels';
import { type NoteDialogMode, WriteNoteDialog } from './write-note-dialog';

/**
 * The encounter's Notes tab: notes newest first, voided ones struck through with their reason. The
 * author edits within 24 hours; after that a correction replaces the note and voids the original.
 */
@Component({
  selector: 'app-encounter-notes',
  imports: [
    NgIcon,
    EmptyState,
    VoidedPill,
    ConfirmAction,
    WriteNoteDialog,
    HlmAlertImports,
    HlmButtonImports,
    HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideFileText, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    @if (canWrite()) {
      <div class="flex justify-end">
        <button hlmBtn type="button" (click)="openDialog('write', null)">
          <ng-icon name="lucidePlus" aria-hidden="true" data-icon="inline-start" />Write note
        </button>
      </div>
    }

    @if (loadError(); as message) {
      <div hlmAlert variant="destructive">
        <ng-icon name="lucideCircleAlert" aria-hidden="true" />
        <h3 hlmAlertTitle>Couldn't load notes</h3>
        <p hlmAlertDescription>{{ message }}</p>
        <div hlmAlertAction>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="notes.reload()">
            Try again
          </button>
        </div>
      </div>
    } @else if (list() === undefined) {
      <div class="flex flex-col gap-3" aria-busy="true">
        <div hlmSkeleton class="h-32 w-full rounded-lg"></div>
        <div hlmSkeleton class="h-32 w-full rounded-lg"></div>
      </div>
    } @else if (list()!.length === 0) {
      <div class="rounded-lg border bg-card">
        <ui-empty-state
          icon="lucideFileText"
          title="No notes yet"
          [description]="
            canWrite()
              ? 'Write the first assessment, progress, nursing or diet note.'
              : 'Assessment, progress, nursing and diet notes appear here.'
          "
        />
      </div>
    } @else {
      <ol class="flex flex-col gap-3" aria-label="Notes, newest first">
        @for (n of list(); track n.id) {
          <li
            class="rounded-lg border bg-card p-4"
            [class.text-muted-foreground]="n.voided"
            [attr.id]="'note-' + n.id"
          >
            <article [attr.aria-labelledby]="'note-heading-' + n.id" tabindex="-1">
              <header class="mb-2 flex flex-wrap items-start justify-between gap-2">
                <div class="min-w-0">
                  <h3
                    [id]="'note-heading-' + n.id"
                    class="font-semibold"
                    [class.line-through]="n.voided"
                  >
                    {{ typeLabel(n.noteType) }} note
                  </h3>
                  <p class="text-xs text-muted-foreground">
                    <span data-numeric>{{ formatDateTime(n.authoredAt) }}</span> ·
                    {{ n.authoredBy }}
                    @if (n.editedAt) {
                      · edited <span data-numeric>{{ formatDateTime(n.editedAt) }}</span>
                    }
                  </p>
                </div>
                <div class="flex flex-wrap items-center gap-1">
                  @if (n.voided; as voided) {
                    <ui-voided-pill [reason]="voided.reason" [by]="voidedBy(voided)" />
                  }
                  @if (n.canEdit) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      type="button"
                      [attr.aria-label]="'Edit ' + typeLabel(n.noteType).toLowerCase() + ' note'"
                      (click)="openDialog('edit', n)"
                    >
                      Edit
                    </button>
                  } @else if (n.canCorrect) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      type="button"
                      [attr.aria-label]="'Correct ' + typeLabel(n.noteType).toLowerCase() + ' note'"
                      (click)="openDialog('correct', n)"
                    >
                      Correct
                    </button>
                  }
                  @if (n.canVoid) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      type="button"
                      [attr.aria-label]="'Void ' + typeLabel(n.noteType).toLowerCase() + ' note'"
                      [uiConfirmAction]="voidNote(n)"
                      confirmTitle="Void this note?"
                      confirmDescription="Use this for a note entered in error, such as on the wrong patient. It stays on the record, struck through, with your reason. To fix what it says, write a correction instead."
                      confirmLabel="Void note"
                      reasonHint="e.g. wrong patient"
                      requireReason
                      destructive
                    >
                      Void
                    </button>
                  }
                </div>
              </header>
              <p class="max-w-[72ch] whitespace-pre-line" [class.line-through]="n.voided">
                {{ n.body }}
              </p>
              @if (n.replacedByNoteId) {
                <p class="mt-2 text-xs">
                  <button
                    type="button"
                    class="cursor-pointer underline underline-offset-2"
                    (click)="jumpTo(n.replacedByNoteId)"
                  >
                    Replaced by a correction
                  </button>
                </p>
              } @else if (n.replacesNoteId) {
                <p class="mt-2 text-xs text-muted-foreground">
                  <button
                    type="button"
                    class="cursor-pointer underline underline-offset-2"
                    (click)="jumpTo(n.replacesNoteId)"
                  >
                    Corrects an earlier note
                  </button>
                </p>
              }
            </article>
          </li>
        }
      </ol>
    }

    <app-write-note-dialog
      [encounterId]="encounterId()"
      [open]="dialogMode() !== null"
      [mode]="dialogMode() ?? 'write'"
      [note]="dialogNote()"
      (closed)="dialogMode.set(null)"
      (saved)="onSaved()"
    />
  `,
})
export class EncounterNotes {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  /** Whether the caller may write a new note (the encounter's `canWriteNotes`). */
  readonly canWrite = input(false);
  /** Reloads the list when it changes, e.g. the encounter's status after a discharge writes its summary. */
  readonly refreshKey = input<unknown>(null);

  protected readonly formatDateTime = formatDateTime;
  protected readonly dialogMode = signal<NoteDialogMode | null>(null);
  protected readonly dialogNote = signal<ClinicalNoteResponse | null>(null);

  protected readonly notes = resource({
    params: () => ({ encounterId: this.encounterId(), refresh: this.refreshKey() }),
    loader: ({ params }) => this.api.invoke(listClinicalNotes, { encounterId: params.encounterId }),
  });

  /** Kept while reloading after a change, so the list doesn't flash back to skeletons. */
  protected readonly list = linkedSignal<
    ClinicalNoteResponse[] | undefined,
    ClinicalNoteResponse[] | undefined
  >({
    source: () => (this.notes.hasValue() ? this.notes.value() : undefined),
    computation: (next, previous) => next ?? (this.notes.error() ? undefined : previous?.value),
  });

  protected readonly loadError = computed(() =>
    this.notes.error() ? (toProblem(this.notes.error()).detail ?? "Couldn't load notes.") : null,
  );

  protected typeLabel(type: string): string {
    return labelOf(ALL_NOTE_TYPES, type);
  }

  protected voidedBy(voided: VoidedResponse): string {
    return [voided.by, formatDateTime(voided.at)].filter(Boolean).join(', ');
  }

  protected openDialog(mode: NoteDialogMode, note: ClinicalNoteResponse | null): void {
    this.dialogNote.set(note);
    this.dialogMode.set(mode);
  }

  /** Scrolls to the related note and moves focus there, so keyboard and screen reader users follow. */
  protected jumpTo(id: string): void {
    const target = document.querySelector<HTMLElement>(`#note-${id} article`);
    target?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    target?.focus({ preventScroll: true });
  }

  protected onSaved(): void {
    this.dialogMode.set(null);
    this.notes.reload();
  }

  protected voidNote(n: ClinicalNoteResponse): (reason: string) => Promise<void> {
    return async (reason) => {
      await this.api.invoke(voidClinicalNote, {
        encounterId: this.encounterId(),
        id: n.id,
        body: { reason },
      });
      toast.success('Note voided');
      this.notes.reload();
    };
  }
}

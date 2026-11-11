import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmButtonImports } from '@hilom/ui/button';
import { HlmDialogImports } from '@hilom/ui/dialog';
import { FormField } from '@hilom/ui/form-field';
import { HlmSelectImports } from '@hilom/ui/select';
import { HlmTextareaImports } from '@hilom/ui/textarea';
import { toast } from '@spartan-ng/brain/sonner';

import { Api } from '../../api/api';
import { correctClinicalNote } from '../../api/fn/clinical/correct-clinical-note';
import { editClinicalNote } from '../../api/fn/clinical/edit-clinical-note';
import { writeClinicalNote } from '../../api/fn/clinical/write-clinical-note';
import { type ClinicalNoteResponse } from '../../api/models/clinical-note-response';
import { type NoteType } from '../../api/models/note-type';
import { type Problem, toProblem } from '../../core/http/problem';
import { ALL_NOTE_TYPES, labelOf, NOTE_TYPES } from './encounter-labels';

/** `write` a new note, `edit` your own within 24 hours, or `correct` one (a new note replaces it). */
export type NoteDialogMode = 'write' | 'edit' | 'correct';

const COPY: Record<
  NoteDialogMode,
  { title: string; description: string; save: string; done: string }
> = {
  write: {
    title: 'Write note',
    description: 'You can edit it for 24 hours. After that, corrections replace it.',
    save: 'Save note',
    done: 'Note saved',
  },
  edit: {
    title: 'Edit note',
    description: 'Edits are possible for 24 hours after writing. The record shows it was edited.',
    save: 'Save changes',
    done: 'Note updated',
  },
  correct: {
    title: 'Write a correction',
    description:
      'Your correction replaces this note. The original stays on the record, struck through, with your reason.',
    save: 'Save correction',
    done: 'Correction saved; the original is voided',
  },
};

/** Writes, edits or corrects a clinical note on an encounter. */
@Component({
  selector: 'app-write-note-dialog',
  imports: [
    ReactiveFormsModule,
    FormField,
    HlmButtonImports,
    HlmDialogImports,
    HlmSelectImports,
    HlmTextareaImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ copy().title }}</h2>
          <p hlmDialogDescription>{{ copy().description }}</p>
        </hlm-dialog-header>
        <form
          id="write-note-form"
          class="flex flex-col gap-4"
          [formGroup]="form"
          (ngSubmit)="save()"
          novalidate
        >
          <ui-form-field label="Kind of note" for="note-type" required>
            <hlm-select formControlName="noteType" [itemToString]="typeLabel">
              <hlm-select-trigger buttonId="note-type" class="w-full sm:w-64">
                <hlm-select-value placeholder="Choose" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                @for (option of types(); track option.value) {
                  <hlm-select-item [value]="option.value">{{ option.label }}</hlm-select-item>
                }
              </hlm-select-content>
            </hlm-select>
          </ui-form-field>
          <ui-form-field label="Note" for="note-body" required>
            <textarea hlmTextarea id="note-body" rows="10" formControlName="body"></textarea>
          </ui-form-field>
          @if (mode() === 'correct') {
            <ui-form-field
              label="What was wrong with the original"
              for="note-reason"
              hint="e.g. wrong patient, wrong findings recorded"
              required
            >
              <textarea hlmTextarea id="note-reason" rows="2" formControlName="reason"></textarea>
            </ui-form-field>
          }
          @if (formError(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
        </form>
        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">Cancel</button>
          <button hlmBtn type="submit" form="write-note-form" [disabled]="saving()">
            {{ saving() ? 'Saving…' : copy().save }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class WriteNoteDialog {
  private readonly api = inject(Api);

  readonly encounterId = input.required<string>();
  readonly open = input(false);
  readonly mode = input<NoteDialogMode>('write');
  /** The note being edited or corrected. */
  readonly note = input<ClinicalNoteResponse | null>(null);
  readonly closed = output();
  readonly saved = output<ClinicalNoteResponse>();

  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly copy = computed(() => COPY[this.mode()]);
  /** A discharge summary keeps its kind when edited; it isn't offered otherwise. */
  protected readonly types = computed(() =>
    this.note()?.noteType === 'discharge_summary' && this.mode() === 'edit'
      ? ALL_NOTE_TYPES.filter((t) => t.value === 'discharge_summary')
      : NOTE_TYPES,
  );
  protected readonly typeLabel = (value: string) => labelOf(ALL_NOTE_TYPES, value);

  protected readonly form = new FormGroup({
    noteType: new FormControl<NoteType | null>(null, Validators.required),
    body: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(10_000)],
    }),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  constructor() {
    // A fresh form every time it opens, starting from the note when editing or correcting.
    effect(() => {
      if (this.open()) {
        const note = this.note();
        const mode = this.mode();
        untracked(() => {
          this.form.reset({
            noteType: mode === 'write' ? null : (note?.noteType ?? null),
            body: mode === 'write' ? '' : (note?.body ?? ''),
            reason: '',
          });
          this.form.controls.reason.setValidators(
            mode === 'correct' ? [Validators.required, Validators.maxLength(500)] : [],
          );
          this.form.controls.reason.updateValueAndValidity();
          this.formError.set(null);
        });
      }
    });
  }

  protected async save(): Promise<void> {
    const { noteType, body, reason } = this.form.getRawValue();
    // Whitespace alone isn't a note or a reason.
    if (!body.trim()) {
      this.form.controls.body.setValue('');
    }
    if (this.mode() === 'correct' && !reason.trim()) {
      this.form.controls.reason.setValue('');
    }
    this.form.markAllAsTouched();
    if (this.form.invalid || !noteType || this.saving()) {
      return;
    }
    this.formError.set(null);
    this.saving.set(true);
    const note = this.note();
    try {
      let saved: ClinicalNoteResponse;
      if (this.mode() === 'edit' && note) {
        saved = await this.api.invoke(editClinicalNote, {
          encounterId: this.encounterId(),
          id: note.id,
          body: { noteType, body, version: note.version },
        });
      } else if (this.mode() === 'correct' && note) {
        saved = await this.api.invoke(correctClinicalNote, {
          encounterId: this.encounterId(),
          id: note.id,
          body: { noteType, body, reason: reason.trim() },
        });
      } else {
        saved = await this.api.invoke(writeClinicalNote, {
          encounterId: this.encounterId(),
          body: { noteType, body },
        });
      }
      toast.success(this.copy().done);
      this.saved.emit(saved);
    } catch (error) {
      this.showProblem(toProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private showProblem(problem: Problem): void {
    let shown = false;
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const control = this.form.get(field);
      if (control && messages[0]) {
        control.setErrors({ server: messages[0] });
        shown = true;
      }
    }
    if (!shown) {
      this.formError.set(
        problem.status === 409 && problem.code === 'concurrency_conflict'
          ? 'Someone changed this note meanwhile. Close this and open it again to see the latest.'
          : (problem.detail ?? "Couldn't save the note."),
      );
    }
  }
}

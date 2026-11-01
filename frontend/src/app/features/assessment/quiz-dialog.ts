import { ChangeDetectorRef, Component, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { DateField } from '../../shared/date-field';
import { BankQuestion, QuestionDraft, QuestionKind, Quiz, QuizQuestion } from './assessment.models';
import { AssessmentService } from './assessment.service';
import { BankPickerDialog } from './bank-picker-dialog';
import { QuestionFields } from './question-fields';
import { QUESTION_KINDS, questionGroup, toDraft } from './question-form';

export interface QuizDialogData {
  cohortId: string;
  quiz: Quiz | null;
}

const toLocal = (value: string | null): string => {
  if (!value) {
    return '';
  }

  const date = new Date(value);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
};

const seed = (q: QuizQuestion): QuestionDraft => ({
  kind: q.kind,
  textAr: q.text.ar,
  textEn: q.text.en,
  points: q.points,
  tolerance: q.tolerance,
  options: q.options.map((o) => ({ textAr: o.text.ar, textEn: o.text.en, isCorrect: o.isCorrect, matchAr: o.matchAr, matchEn: o.matchEn })),
});

const toIso = (value: string): string | null => (value ? new Date(value).toISOString() : null);

@Component({
  selector: 'app-quiz-dialog',
  imports: [
    DateField,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatCheckboxModule,
    QuestionFields,
    ReactiveFormsModule,
    TranslatePipe,
  ],
  templateUrl: './quiz-dialog.html',
  styleUrl: './quiz-dialog.scss',
})
export class QuizDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly fb = this.formBuilder.nonNullable;
  private readonly pickers = inject(MatDialog);
  private readonly changes = inject(ChangeDetectorRef);
  private readonly assessment = inject(AssessmentService);
  private readonly dialog = inject(MatDialogRef<QuizDialog, boolean>);
  private readonly data = inject<QuizDialogData>(MAT_DIALOG_DATA);

  protected readonly kindList = QUESTION_KINDS;
  protected readonly quiz = this.data.quiz;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = this.fb.group({
    titleAr: [this.quiz?.title.ar ?? '', [Validators.required, Validators.maxLength(200)]],
    titleEn: [this.quiz?.title.en ?? '', [Validators.required, Validators.maxLength(200)]],
    timeLimitMinutes: [this.quiz?.timeLimitMinutes ?? 20, [Validators.required, Validators.min(1)]],
    maxAttempts: [this.quiz?.maxAttempts ?? 1, [Validators.required, Validators.min(1)]],
    weight: [this.quiz?.weight ?? 10, [Validators.required, Validators.min(1)]],
    opensAt: [toLocal(this.quiz?.opensAt ?? null)],
    closesAt: [toLocal(this.quiz?.closesAt ?? null)],
    drawCount: [this.quiz?.drawCount ?? (null as number | null), [Validators.min(1)]],
    shuffle: [this.quiz?.shuffle ?? false],
    examKey: [this.quiz?.examKey ?? '', [Validators.pattern(/^\s*[0-9a-fA-F]{64}\s*$/)]],
    questions: this.fb.array((this.quiz?.questions ?? []).map((q) => questionGroup(this.formBuilder, seed(q)))),
  });

  protected get questions(): FormArray<FormGroup> {
    return this.form.controls.questions as unknown as FormArray<FormGroup>;
  }

  protected addQuestion(kind: QuestionKind): void {
    this.questions.push(questionGroup(this.formBuilder, kind));
  }

  protected removeQuestion(index: number): void {
    this.questions.removeAt(index);
  }

  protected async pickFromBank(): Promise<void> {
    const ref = this.pickers.open(BankPickerDialog, { width: '44rem', maxWidth: 'calc(100vw - 2rem)', data: { cohortId: this.data.cohortId } });
    const picked: BankQuestion[] | undefined = await firstValueFrom(ref.afterClosed());

    for (const question of picked ?? []) {
      this.questions.push(questionGroup(this.formBuilder, question));
    }

    // Nothing else tells the view the form grew after the picker closed.
    this.changes.markForCheck();
  }

  protected async save(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(this.assessment.saveQuiz(this.data.cohortId, this.quiz?.id ?? null, this.toInput()));
      this.dialog.close(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  private toInput() {
    const value = this.form.getRawValue();

    return {
      titleAr: value.titleAr,
      titleEn: value.titleEn,
      timeLimitMinutes: value.timeLimitMinutes,
      maxAttempts: value.maxAttempts,
      weight: value.weight,
      opensAt: toIso(value.opensAt),
      closesAt: toIso(value.closesAt),
      drawCount: value.drawCount ? Number(value.drawCount) : null,
      shuffle: value.shuffle,
      examKey: value.examKey?.trim() || null,
      questions: this.questions.controls.map(toDraft),
    };
  }
}

import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { Attempt, AttemptQuestion } from './assessment.models';
import { AssessmentService } from './assessment.service';

const TYPING_PAUSE_MS = 800;

@Component({
  selector: 'app-take-quiz',
  imports: [LocalizedPipe, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatRadioModule, MatSelectModule, StatePanel, TranslatePipe],
  templateUrl: './take-quiz.html',
  styleUrl: './take-quiz.scss',
})
export class TakeQuiz implements OnInit, OnDestroy {
  private readonly assessment = inject(AssessmentService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private timer?: ReturnType<typeof setInterval>;
  private saving = Promise.resolve();
  private readonly typing = new Map<string, ReturnType<typeof setTimeout>>();

  readonly cohortId = input.required<string>();
  readonly quizId = input.required<string>();

  protected readonly attempt = signal<Attempt | null>(null);
  protected readonly failed = signal<string | null>(null);
  protected readonly secondsLeft = signal(0);
  protected readonly answers = signal<Record<string, string>>({});
  protected readonly unsaved = signal(0);
  protected readonly busy = signal(false);

  protected readonly clock = computed(() => {
    const total = this.secondsLeft();
    const minutes = String(Math.floor(total / 60)).padStart(2, '0');
    const seconds = String(total % 60).padStart(2, '0');
    return `${minutes}:${seconds}`;
  });

  protected readonly answered = computed(() => Object.keys(this.answers()).length);

  async ngOnInit(): Promise<void> {
    try {
      const attempt = await firstValueFrom(this.assessment.startAttempt(this.cohortId(), this.quizId()));
      this.attempt.set(attempt);
      this.answers.set(Object.fromEntries(attempt.questions.filter((q) => q.response).map((q) => [q.id, q.response!])));

      // The server sends the time left, so a wrong device clock cannot change it.
      const endsAt = Date.now() + attempt.secondsLeft * 1000;
      this.secondsLeft.set(attempt.secondsLeft);
      this.timer = setInterval(() => this.tick(endsAt), 1000);
    } catch (error) {
      this.failed.set(errorKey(error));
    }
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected async choose(question: AttemptQuestion, response: string): Promise<void> {
    clearTimeout(this.typing.get(question.id));
    this.typing.delete(question.id);
    this.remember(question, response);
    this.unsaved.update((count) => count + 1);

    // One save at a time, always with the newest answer, so quick clicks cannot overtake each other.
    this.saving = this.saving.then(async () => {
      try {
        await firstValueFrom(this.assessment.saveAnswer(this.cohortId(), this.attempt()!.id, question.id, this.answers()[question.id] ?? ''));
      } catch (error) {
        this.notify(errorKey(error));
      } finally {
        this.unsaved.update((count) => count - 1);
      }
    });

    await this.saving;
  }

  protected isPicked(question: AttemptQuestion, optionId: string): boolean {
    return this.picks(question).includes(optionId);
  }

  protected togglePick(question: AttemptQuestion, optionId: string): Promise<void> {
    const picks = this.picks(question);
    const next = picks.includes(optionId) ? picks.filter((id) => id !== optionId) : [...picks, optionId];
    return this.choose(question, next.join(','));
  }

  /** Typing saves after a short pause, and at once on leaving the box. */
  protected draft(question: AttemptQuestion, text: string): void {
    this.remember(question, text);
    clearTimeout(this.typing.get(question.id));
    this.typing.set(question.id, setTimeout(() => void this.choose(question, text), TYPING_PAUSE_MS));
  }

  protected flush(question: AttemptQuestion): void {
    if (this.typing.has(question.id)) {
      void this.choose(question, this.answers()[question.id] ?? '');
    }
  }

  protected pairOf(question: AttemptQuestion, optionId: string): string | undefined {
    return this.pairs(question).get(optionId);
  }

  protected setPair(question: AttemptQuestion, optionId: string, key: string): Promise<void> {
    const pairs = this.pairs(question).set(optionId, key);
    return this.choose(question, [...pairs].map(([left, right]) => `${left}:${right}`).join(','));
  }

  private picks(question: AttemptQuestion): string[] {
    return (this.answers()[question.id] ?? '').split(',').filter(Boolean);
  }

  private pairs(question: AttemptQuestion): Map<string, string> {
    return new Map(this.picks(question).map((pair) => pair.split(':') as [string, string]));
  }

  private remember(question: AttemptQuestion, response: string): void {
    this.answers.update((current) => {
      const next = { ...current };

      if (response.trim()) {
        next[question.id] = response;
      } else {
        delete next[question.id];
      }

      return next;
    });
  }

  protected async confirmSubmit(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'quiz.submitTitle', messageKey: 'quiz.submitMessage', confirmKey: 'quiz.submit' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.finish('quiz.submitted');
    }
  }

  protected back(): void {
    void this.router.navigate(['/learn', this.cohortId(), 'assessments']);
  }

  private tick(endsAt: number): void {
    const left = Math.max(0, Math.ceil((endsAt - Date.now()) / 1000));
    this.secondsLeft.set(left);

    if (left === 0) {
      clearInterval(this.timer);
      this.dialog.closeAll();
      void this.finish('quiz.timeUp');
    }
  }

  private async finish(messageKey: string): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      // Anything still waiting on the typing pause is saved before the attempt closes.
      await Promise.all(this.attempt()!.questions.filter((q) => this.typing.has(q.id)).map((q) => this.choose(q, this.answers()[q.id] ?? '')));
      await this.saving;
      await firstValueFrom(this.assessment.submitAttempt(this.cohortId(), this.attempt()!.id));
      this.notify(messageKey);
      this.back();
    } catch (error) {
      this.notify(errorKey(error));
      this.busy.set(false);
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 5000 });
  }
}

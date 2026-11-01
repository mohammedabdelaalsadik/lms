import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { Assignment, Quiz } from './assessment.models';
import { AssessmentService, staffUrl } from './assessment.service';
import { AssignmentDialog } from './assignment-dialog';
import { QuizDialog } from './quiz-dialog';

@Component({
  selector: 'app-cohort-assessments',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  templateUrl: './cohort-assessments.html',
  styles: `
    .back {
      align-self: flex-start;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    h2.section-title {
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--line);
      font: var(--mat-sys-title-medium);
      font-weight: 600;
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    li small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
    .tag {
      padding: 2px var(--space-2);
      border-radius: var(--radius-s);
      background: var(--mat-sys-surface-container-high);
      font: var(--mat-sys-label-medium);
    }
  `,
})
export class CohortAssessments {
  private readonly assessment = inject(AssessmentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly quizzes = httpResource<Quiz[]>(() => `${staffUrl(this.id())}/quizzes`);
  protected readonly assignments = httpResource<Assignment[]>(() => `${staffUrl(this.id())}/assignments`);
  protected readonly quizList = computed(() => (this.quizzes.hasValue() ? this.quizzes.value() : null));
  protected readonly assignmentList = computed(() => (this.assignments.hasValue() ? this.assignments.value() : null));

  protected hasEssay(quiz: Quiz): boolean {
    return quiz.questions.some((q) => q.kind === 'Essay');
  }

  protected async openQuiz(quiz: Quiz | null): Promise<void> {
    const ref = this.dialog.open(QuizDialog, {
      data: { cohortId: this.id(), quiz },
      width: '52rem',
      maxWidth: 'calc(100vw - 2rem)',
    });

    if (await firstValueFrom(ref.afterClosed())) {
      this.quizzes.reload();
    }
  }

  protected async openAssignment(): Promise<void> {
    const ref = this.dialog.open(AssignmentDialog, {
      data: this.id(),
      width: '40rem',
      maxWidth: 'calc(100vw - 2rem)',
    });

    if (await firstValueFrom(ref.afterClosed())) {
      this.assignments.reload();
    }
  }

  protected async publish(quiz: Quiz): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'assessments.publishTitle', messageKey: 'assessments.publishMessage', confirmKey: 'courses.publish' },
    });

    if (!(await firstValueFrom(ref.afterClosed()))) {
      return;
    }

    try {
      await firstValueFrom(this.assessment.publishQuiz(this.id(), quiz.id));
      this.notify('assessments.published');
      this.quizzes.reload();
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

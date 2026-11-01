import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { staffUrl } from './assessment.service';

interface Essay {
  attemptId: string;
  questionId: string;
  trainee: LocalizedText;
  question: LocalizedText;
  answer: string;
  maxPoints: number;
  awarded: number | null;
}

@Component({
  selector: 'app-essay-marking',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/cohorts', id(), 'assessments']" class="back">
        <mat-icon class="flip-rtl">arrow_back</mat-icon>
        {{ 'assessments.title' | translate }}
      </a>

      <header class="page-header">
        <div>
          <h1>{{ 'marking.title' | translate }}</h1>
          <p>{{ 'marking.subtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      }

      @for (item of items(); track item.attemptId + item.questionId) {
        <article class="panel essay">
          <p class="who"><strong>{{ item.trainee | localized }}</strong></p>
          <p class="prompt">{{ item.question | localized }}</p>
          <p class="answer">{{ item.answer }}</p>
          <form (submit)="$event.preventDefault(); save(item, box.value)">
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'marking.points' | translate: { max: item.maxPoints } }}</mat-label>
              <input #box matInput type="number" min="0" step="0.5" [max]="item.maxPoints" [value]="item.awarded ?? ''" />
            </mat-form-field>
            <button matButton="filled" type="submit">{{ 'common.save' | translate }}</button>
          </form>
        </article>
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .essay {
      display: grid;
      gap: var(--space-2);
      padding: var(--space-4);
    }
    p {
      margin: 0;
    }
    .prompt {
      color: var(--mat-sys-on-surface-variant);
    }
    .answer {
      padding: var(--space-3);
      border-radius: var(--radius-s);
      background: var(--mat-sys-surface-container-low);
      overflow-wrap: anywhere;
      white-space: pre-wrap;
    }
    form {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
  `,
})
export class EssayMarking {
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();
  readonly quizId = input.required<string>();

  protected readonly resource = httpResource<Essay[]>(() => `${staffUrl(this.id())}/quizzes/${this.quizId()}/essays`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected async save(item: Essay, value: string): Promise<void> {
    if (value === '') {
      return;
    }

    try {
      await firstValueFrom(this.http.put(`${staffUrl(this.id())}/attempts/${item.attemptId}/essays/${item.questionId}`, { points: Number(value) }));
      this.resource.reload();
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
    }
  }
}

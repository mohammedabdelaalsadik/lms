import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { downloadFile } from '../../core/http/download';
import { LocalizedText } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { FormQuestionKind } from './learning.models';
import { LEARNING_URL } from './learning.service';

interface QuestionResult {
  kind: FormQuestionKind;
  text: LocalizedText;
  answered: number;
  options: { text: LocalizedText; count: number }[];
  average: number | null;
  answers: { name: LocalizedText | null; text: string }[];
}

interface FormResult {
  activityId: string;
  title: LocalizedText;
  isAnonymous: boolean;
  responses: number;
  enrolled: number;
  isHidden: boolean;
  questions: QuestionResult[];
}

@Component({
  selector: 'app-form-results',
  imports: [LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/cohorts', id()]" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'cohorts.roster' | translate }}</a>

      <header class="page-header">
        <div>
          <h1>{{ 'forms.resultsTitle' | translate }}</h1>
          <p>{{ 'forms.resultsSubtitle' | translate }}</p>
        </div>
        <button matButton="outlined" (click)="exportCsv()"><mat-icon>download</mat-icon>{{ 'reports.exportCsv' | translate }}</button>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        @for (form of items(); track form.activityId) {
          <section class="panel form">
            <header>
              <h2>{{ form.title | localized }}</h2>
              <small>
                {{ 'forms.responses' | translate: { count: form.responses, total: form.enrolled } }}
                @if (form.isAnonymous) {
                  · {{ 'forms.anonymous' | translate }}
                }
              </small>
            </header>
            @if (form.isHidden) {
              <p class="hidden-note">{{ 'forms.hidden' | translate }}</p>
            }
            @for (question of form.questions; track $index) {
              <div class="question">
                <h3>{{ question.text | localized }}</h3>
                @if (question.average !== null) {
                  <small>{{ 'forms.average' | translate: { value: question.average } }}</small>
                }
                @for (option of question.options; track $index) {
                  <div class="bar-row">
                    <span class="label">{{ option.text | localized }}</span>
                    <span class="track" aria-hidden="true"><span class="fill" [style.inline-size.%]="share(option.count, question.answered)"></span></span>
                    <span class="count">{{ option.count }}</span>
                  </div>
                }
                @for (answer of question.answers; track $index) {
                  <blockquote>
                    {{ answer.text }}
                    @if (answer.name) {
                      <small>{{ answer.name | localized }}</small>
                    }
                  </blockquote>
                }
              </div>
            }
          </section>
        }
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .form > header,
    .question,
    .hidden-note {
      padding: var(--space-3) var(--space-4);
    }
    .question {
      display: grid;
      gap: var(--space-1);
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
    h2,
    h3,
    .hidden-note,
    blockquote {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-medium);
    }
    h3 {
      font: var(--mat-sys-title-small);
    }
    small,
    .hidden-note {
      color: var(--mat-sys-on-surface-variant);
    }
    small {
      display: block;
    }
    .bar-row {
      display: grid;
      grid-template-columns: minmax(6rem, 14rem) 1fr 2rem;
      align-items: center;
      gap: var(--space-2);
    }
    .track {
      height: 0.625rem;
      overflow: hidden;
      border-radius: var(--radius-s);
      background: var(--mat-sys-surface-container);
    }
    .fill {
      display: block;
      height: 100%;
      background: var(--brand-primary);
    }
    .count {
      text-align: end;
    }
    blockquote {
      padding-inline-start: var(--space-3);
      border-inline-start: 3px solid var(--mat-sys-outline-variant);
    }
  `,
})
export class FormResults {
  private readonly http = inject(HttpClient);

  readonly id = input.required<string>();

  protected readonly resource = httpResource<FormResult[]>(() => `${LEARNING_URL}/cohorts/${this.id()}/forms`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected exportCsv(): Promise<void> {
    return downloadFile(this.http, `${LEARNING_URL}/cohorts/${this.id()}/forms/export`, 'form-results.csv');
  }

  protected share(count: number, total: number): number {
    return total ? (count / total) * 100 : 0;
  }
}

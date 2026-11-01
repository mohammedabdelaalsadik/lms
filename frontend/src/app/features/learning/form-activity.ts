import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LanguageService } from '../../core/i18n/language.service';
import { FormDefinition, FormQuestion } from './learning.models';
import { LEARNING_URL } from './learning.service';

interface Answer {
  question: number;
  options?: number[];
  rating?: number;
  text?: string;
}

/** The trainee's side of a form. Answers go in once and cannot be changed. */
@Component({
  selector: 'app-form-activity',
  imports: [MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatRadioModule, TranslatePipe],
  template: `
    @if (submitted()) {
      <p class="sent"><mat-icon aria-hidden="true">check_circle</mat-icon>{{ 'forms.thanks' | translate }}</p>
    } @else {
      @if (form().isAnonymous) {
        <p class="note"><mat-icon aria-hidden="true">visibility_off</mat-icon>{{ 'forms.anonymousNote' | translate }}</p>
      }

      @for (question of form().questions; track $index; let index = $index) {
        <fieldset>
          <legend>{{ text(question) }}@if (question.isRequired) { <span aria-hidden="true"> *</span> }</legend>
          @switch (question.kind) {
            @case ('Single') {
              <mat-radio-group [attr.aria-label]="text(question)" (change)="pick(index, [$event.value])">
                @for (option of question.options; track $index; let optionIndex = $index) {
                  <mat-radio-button [value]="optionIndex">{{ label(option) }}</mat-radio-button>
                }
              </mat-radio-group>
            }
            @case ('Multiple') {
              @for (option of question.options; track $index; let optionIndex = $index) {
                <mat-checkbox (change)="toggle(index, optionIndex, $event.checked)">{{ label(option) }}</mat-checkbox>
              }
            }
            @case ('Rating') {
              <mat-radio-group class="rating" [attr.aria-label]="text(question)" (change)="rate(index, $event.value)">
                @for (value of ratings; track value) {
                  <mat-radio-button [value]="value">{{ value }}</mat-radio-button>
                }
              </mat-radio-group>
            }
            @default {
              <mat-form-field subscriptSizing="dynamic">
                <textarea matInput rows="3" maxlength="2000" [attr.aria-label]="text(question)" (input)="write(index, $any($event.target).value)"></textarea>
              </mat-form-field>
            }
          }
        </fieldset>
      }

      @if (error(); as key) {
        <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
      }
      <button matButton="filled" [disabled]="busy()" (click)="send()">{{ 'forms.send' | translate }}</button>
    }
  `,
  styles: `
    :host {
      display: grid;
      width: 100%;
      justify-items: start;
      gap: var(--space-3);
    }
    fieldset {
      display: grid;
      gap: var(--space-1);
      width: 100%;
      margin: 0;
      padding: 0;
      border: 0;
    }
    legend {
      padding: 0;
      margin-block-end: var(--space-1);
      font: var(--mat-sys-title-small);
    }
    mat-radio-group {
      display: grid;
    }
    mat-radio-button,
    mat-checkbox {
      justify-self: start;
    }
    .rating {
      display: flex;
      flex-wrap: wrap;
    }
    mat-form-field {
      width: 100%;
    }
    .sent,
    .note {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
    }
    .sent mat-icon {
      color: var(--brand-primary);
    }
    .note {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class FormActivity {
  private readonly http = inject(HttpClient);
  private readonly language = inject(LanguageService);

  readonly cohortId = input.required<string>();
  readonly activityId = input.required<string>();
  readonly form = input.required<FormDefinition>();
  readonly isDone = input(false);
  readonly sent = output<void>();

  protected readonly ratings = [1, 2, 3, 4, 5];
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly justSent = signal(false);
  protected readonly submitted = computed(() => this.isDone() || this.justSent());
  private readonly answers = new Map<number, Answer>();

  protected text(question: FormQuestion): string {
    return this.language.language() === 'ar' ? question.textAr : question.textEn;
  }

  protected label(option: { ar: string; en: string }): string {
    return this.language.language() === 'ar' ? option.ar : option.en;
  }

  protected pick(question: number, options: number[]): void {
    this.answers.set(question, { question, options });
  }

  protected toggle(question: number, option: number, checked: boolean): void {
    const current = this.answers.get(question)?.options ?? [];
    this.pick(question, checked ? [...current, option] : current.filter((o) => o !== option));
  }

  protected rate(question: number, rating: number): void {
    this.answers.set(question, { question, rating });
  }

  protected write(question: number, text: string): void {
    this.answers.set(question, { question, text });
  }

  protected async send(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      const url = `${LEARNING_URL}/cohorts/${this.cohortId()}/activities/${this.activityId()}/form`;
      await firstValueFrom(this.http.post(url, { answers: [...this.answers.values()] }));
      this.justSent.set(true);
      this.sent.emit();
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}

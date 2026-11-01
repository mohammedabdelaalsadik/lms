import { Component, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe } from '@ngx-translate/core';
import { FormDefinition, FormQuestionKind } from './learning.models';

interface Draft {
  key: number;
  kind: FormQuestionKind;
  textAr: string;
  textEn: string;
  isRequired: boolean;
  optionsAr: string;
  optionsEn: string;
}

const lines = (text: string): string[] =>
  text
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);

/** Options are typed one per line, so a list can be pasted in. Each language needs the same number of lines. */
@Component({
  selector: 'app-form-builder',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <mat-slide-toggle class="anonymous" [checked]="anonymous()" (change)="anonymous.set($event.checked)">{{ 'forms.anonymous' | translate }}</mat-slide-toggle>
    <p class="hint">{{ 'forms.lockedNote' | translate }}</p>

    @for (question of questions(); track question.key; let index = $index) {
      <fieldset>
        <legend>{{ 'forms.question' | translate }} {{ index + 1 }}</legend>
        <div class="row">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'forms.kind' | translate }}</mat-label>
            <mat-select [value]="question.kind" (selectionChange)="change(question, { kind: $event.value })">
              @for (kind of kinds; track kind) {
                <mat-option [value]="kind">{{ 'forms.kind' + kind | translate }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-slide-toggle [checked]="question.isRequired" (change)="change(question, { isRequired: $event.checked })">{{ 'forms.required' | translate }}</mat-slide-toggle>
          <button matIconButton type="button" [attr.aria-label]="'common.remove' | translate" (click)="remove(question)"><mat-icon>delete</mat-icon></button>
        </div>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'forms.textAr' | translate }}</mat-label>
          <input matInput dir="rtl" maxlength="500" [value]="question.textAr" (input)="change(question, { textAr: $any($event.target).value })" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'forms.textEn' | translate }}</mat-label>
          <input matInput dir="ltr" maxlength="500" [value]="question.textEn" (input)="change(question, { textEn: $any($event.target).value })" />
        </mat-form-field>
        @if (question.kind === 'Single' || question.kind === 'Multiple') {
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'forms.optionsAr' | translate }}</mat-label>
            <textarea matInput dir="rtl" rows="3" [value]="question.optionsAr" (input)="change(question, { optionsAr: $any($event.target).value })"></textarea>
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'forms.optionsEn' | translate }}</mat-label>
            <textarea matInput dir="ltr" rows="3" [value]="question.optionsEn" (input)="change(question, { optionsEn: $any($event.target).value })"></textarea>
          </mat-form-field>
        }
      </fieldset>
    }

    <button matButton type="button" (click)="add()"><mat-icon>add</mat-icon>{{ 'forms.addQuestion' | translate }}</button>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-2);
    }
    .anonymous {
      justify-self: start;
    }
    .hint {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
    fieldset {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: var(--space-3);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-s);
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);

      mat-form-field {
        flex: 1 1 10rem;
      }
    }
  `,
})
export class FormBuilderField {
  private nextKey = 1;

  protected readonly kinds: FormQuestionKind[] = ['Single', 'Multiple', 'Rating', 'Text'];
  protected readonly anonymous = signal(false);
  protected readonly questions = signal<Draft[]>([this.blank()]);

  /** Null until every question is complete in both languages. */
  readonly value = computed<FormDefinition | null>(() => {
    const questions = this.questions();
    const complete = questions.length > 0 && questions.every((q) => this.isComplete(q));

    if (!complete) {
      return null;
    }

    return {
      isAnonymous: this.anonymous(),
      questions: questions.map((q) => {
        const ar = lines(q.optionsAr);
        const en = lines(q.optionsEn);
        const choice = q.kind === 'Single' || q.kind === 'Multiple';
        return {
          kind: q.kind,
          textAr: q.textAr.trim(),
          textEn: q.textEn.trim(),
          isRequired: q.isRequired,
          options: choice ? ar.map((text, index) => ({ ar: text, en: en[index] })) : [],
        };
      }),
    };
  });

  protected add(): void {
    this.questions.update((list) => [...list, this.blank()]);
  }

  protected remove(question: Draft): void {
    this.questions.update((list) => list.filter((q) => q.key !== question.key));
  }

  protected change(question: Draft, patch: Partial<Draft>): void {
    this.questions.update((list) => list.map((q) => (q.key === question.key ? { ...q, ...patch } : q)));
  }

  private isComplete(q: Draft): boolean {
    if (!q.textAr.trim() || !q.textEn.trim()) {
      return false;
    }

    if (q.kind !== 'Single' && q.kind !== 'Multiple') {
      return true;
    }

    const count = lines(q.optionsAr).length;
    return count >= 2 && count === lines(q.optionsEn).length;
  }

  private blank(): Draft {
    return { key: this.nextKey++, kind: 'Single', textAr: '', textEn: '', isRequired: true, optionsAr: '', optionsEn: '' };
  }
}

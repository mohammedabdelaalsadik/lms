import { Component, inject, input } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslatePipe } from '@ngx-translate/core';
import { QuestionKind } from './assessment.models';
import { optionGroup, optionsOf } from './question-form';

/** The inputs of one question, adapted to its kind. */
@Component({
  selector: 'app-question-fields',
  imports: [
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    ReactiveFormsModule,
    TranslatePipe,
  ],
  template: `
    <div [formGroup]="group()" class="fields">
      <div class="two-up">
        <mat-form-field>
          <mat-label>{{ 'assessments.questionAr' | translate }}</mat-label>
          <textarea matInput dir="rtl" rows="2" formControlName="textAr"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'assessments.questionEn' | translate }}</mat-label>
          <textarea matInput dir="ltr" rows="2" formControlName="textEn"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
      </div>

      @switch (kind) {
        @case ('Essay') {
          <p class="hint">{{ 'bank.essayHelp' | translate }}</p>
        }
        @case ('Numeric') {
          <div class="two-up" formArrayName="options">
            <mat-form-field [formGroupName]="0">
              <mat-label>{{ 'bank.numericAnswer' | translate }}</mat-label>
              <input matInput dir="ltr" formControlName="textEn" />
              <mat-error>{{ 'bank.numberNeeded' | translate }}</mat-error>
            </mat-form-field>
            <mat-form-field [formGroup]="group()">
              <mat-label>{{ 'bank.tolerance' | translate }}</mat-label>
              <input matInput type="number" min="0" step="any" formControlName="tolerance" />
            </mat-form-field>
          </div>
        }
        @case ('Matching') {
          <p class="hint">{{ 'bank.matchingHelp' | translate }}</p>
          <div formArrayName="options">
            @for (option of options.controls; track option; let o = $index) {
              <div class="option" [formGroupName]="o">
                <mat-form-field><mat-label>{{ 'bank.leftAr' | translate }}</mat-label><input matInput dir="rtl" formControlName="textAr" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'bank.leftEn' | translate }}</mat-label><input matInput dir="ltr" formControlName="textEn" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'bank.rightAr' | translate }}</mat-label><input matInput dir="rtl" formControlName="matchAr" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'bank.rightEn' | translate }}</mat-label><input matInput dir="ltr" formControlName="matchEn" /></mat-form-field>
                @if (options.length > 2) {
                  <button matIconButton type="button" (click)="remove(o)" [attr.aria-label]="'common.remove' | translate"><mat-icon>close</mat-icon></button>
                }
              </div>
            }
          </div>
        }
        @case ('ShortAnswer') {
          <p class="hint">{{ 'bank.shortHelp' | translate }}</p>
          <div formArrayName="options">
            @for (option of options.controls; track option; let o = $index) {
              <div class="option" [formGroupName]="o">
                <mat-form-field><mat-label>{{ 'bank.acceptedAr' | translate }}</mat-label><input matInput dir="rtl" formControlName="textAr" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'bank.acceptedEn' | translate }}</mat-label><input matInput dir="ltr" formControlName="textEn" /></mat-form-field>
                @if (options.length > 1) {
                  <button matIconButton type="button" (click)="remove(o)" [attr.aria-label]="'common.remove' | translate"><mat-icon>close</mat-icon></button>
                }
              </div>
            }
          </div>
        }
        @case ('MultipleResponse') {
          <p class="hint">{{ 'bank.multiHelp' | translate }}</p>
          <div formArrayName="options">
            @for (option of options.controls; track option; let o = $index) {
              <div class="option" [formGroupName]="o">
                <mat-checkbox formControlName="isCorrect" [attr.aria-label]="'assessments.correctAnswer' | translate" />
                <mat-form-field><mat-label>{{ 'assessments.optionAr' | translate }}</mat-label><input matInput dir="rtl" formControlName="textAr" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'assessments.optionEn' | translate }}</mat-label><input matInput dir="ltr" formControlName="textEn" /></mat-form-field>
                @if (options.length > 2) {
                  <button matIconButton type="button" (click)="remove(o)" [attr.aria-label]="'common.remove' | translate"><mat-icon>close</mat-icon></button>
                }
              </div>
            }
          </div>
        }
        @default {
          <mat-radio-group formControlName="correct" [attr.aria-label]="'assessments.correctAnswer' | translate">
            <div formArrayName="options">
              @for (option of options.controls; track option; let o = $index) {
                <div class="option" [formGroupName]="o">
                  <mat-radio-button [value]="o" [attr.aria-label]="'assessments.correctAnswer' | translate" />
                  <mat-form-field><mat-label>{{ 'assessments.optionAr' | translate }}</mat-label><input matInput dir="rtl" formControlName="textAr" /></mat-form-field>
                  <mat-form-field><mat-label>{{ 'assessments.optionEn' | translate }}</mat-label><input matInput dir="ltr" formControlName="textEn" /></mat-form-field>
                  @if (kind === 'MultipleChoice' && options.length > 2) {
                    <button matIconButton type="button" (click)="remove(o)" [attr.aria-label]="'common.remove' | translate"><mat-icon>close</mat-icon></button>
                  }
                </div>
              }
            </div>
          </mat-radio-group>
        }
      }

      <div class="foot">
        @if (kind !== 'TrueFalse' && kind !== 'Numeric' && kind !== 'Essay') {
          <button matButton type="button" (click)="add()"><mat-icon>add</mat-icon>{{ addLabel | translate }}</button>
        }
        <mat-form-field class="points">
          <mat-label>{{ 'assessments.points' | translate }}</mat-label>
          <input matInput type="number" min="1" formControlName="points" />
        </mat-form-field>
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    .fields {
      display: grid;
      gap: var(--space-3);
    }
    .two-up {
      display: grid;
      gap: var(--space-3);

      @media (min-width: 600px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    .hint {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
    .option {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin-block-end: var(--space-2);

      mat-form-field {
        flex: 1 1 10rem;
      }
    }
    .foot {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
    .points {
      width: 7rem;
    }
  `,
})
export class QuestionFields {
  private readonly fb = inject(FormBuilder);

  readonly group = input.required<FormGroup>();

  protected get kind(): QuestionKind {
    return this.group().controls['kind'].value;
  }

  protected get options() {
    return optionsOf(this.group());
  }

  protected get addLabel(): string {
    return this.kind === 'Matching' ? 'bank.addPair' : this.kind === 'ShortAnswer' ? 'bank.addAccepted' : 'assessments.addOption';
  }

  protected add(): void {
    this.options.push(optionGroup(this.fb, this.kind));
  }

  protected remove(index: number): void {
    const correct = this.group().controls['correct'];
    this.options.removeAt(index);

    if (correct.value >= this.options.length) {
      correct.setValue(0);
    }
  }
}

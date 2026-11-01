import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/language.service';
import { StatePanel } from '../../shared/state-panel';
import { BankList, BankQuestion } from './assessment.models';
import { staffUrl } from './assessment.service';

/** Chooses bank questions for a quiz. The quiz keeps its own copies. */
@Component({
  selector: 'app-bank-picker-dialog',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'bank.pickTitle' | translate }}</h2>
    <mat-dialog-content>
      <div class="filters">
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'bank.search' | translate }}</mat-label>
          <input matInput name="search" maxlength="100" [ngModel]="search()" (ngModelChange)="search.set($event)" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'bank.category' | translate }}</mat-label>
          <mat-select [value]="category()" (selectionChange)="category.set($event.value)">
            <mat-option value="">{{ 'filters.allCategories' | translate }}</mat-option>
            @for (name of data()?.categories; track name) {
              <mat-option [value]="name">{{ name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </div>

      @if (resource.error()) {
        <app-state-panel kind="error" (retry)="resource.reload()" />
      } @else if (!data()) {
        <app-state-panel kind="loading" />
      } @else if (data()!.questions.items.length === 0) {
        <app-state-panel kind="empty" />
      } @else {
        <ul>
          @for (question of data()!.questions.items; track question.id) {
            <li>
              <mat-checkbox [checked]="picked().has(question.id)" (change)="toggle(question)">
                {{ label(question) }}
                <small>{{ 'bank.kind.' + question.kind | translate }}</small>
              </mat-checkbox>
            </li>
          }
        </ul>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" [disabled]="picked().size === 0" (click)="done()">
        {{ 'bank.addPicked' | translate: { count: picked().size } }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .filters {
      display: grid;
      gap: var(--space-3);
      padding-block: var(--space-2) var(--space-3);

      @media (min-width: 600px) {
        grid-template-columns: 2fr 1fr;
      }
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      padding-block: var(--space-1);
      border-block-end: 1px solid var(--line);
    }
    small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class BankPickerDialog {
  private readonly data$ = inject<{ cohortId: string }>(MAT_DIALOG_DATA);
  private readonly dialog = inject(MatDialogRef<BankPickerDialog, BankQuestion[]>);
  private readonly languages = inject(LanguageService);

  protected readonly search = signal('');
  protected readonly category = signal('');
  protected readonly picked = signal(new Map<string, BankQuestion>());
  protected readonly resource = httpResource<BankList>(() => ({
    url: `${staffUrl(this.data$.cohortId)}/question-bank`,
    params: { search: this.search().trim(), category: this.category(), pageSize: 100 },
  }));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected label(question: BankQuestion): string {
    return this.languages.language() === 'ar' ? question.textAr : question.textEn;
  }

  protected toggle(question: BankQuestion): void {
    this.picked.update((current) => {
      const next = new Map(current);

      if (!next.delete(question.id)) {
        next.set(question.id, question);
      }

      return next;
    });
  }

  protected done(): void {
    this.dialog.close([...this.picked().values()]);
  }
}

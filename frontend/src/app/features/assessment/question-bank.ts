import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { urlFilters } from '../../core/filters/url-filters';
import { errorKey } from '../../core/http/api-error';
import { LanguageService } from '../../core/i18n/language.service';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { FilterBar } from '../../shared/filter-bar';
import { StatePanel } from '../../shared/state-panel';
import { BankList, BankQuestion, QuestionKind } from './assessment.models';
import { QuestionFields } from './question-fields';
import { QUESTION_KINDS, questionGroup, toDraft } from './question-form';

const bankUrl = (courseId: string) => `/api/v1/question-bank/${courseId}`;

interface EditData {
  courseId: string;
  question: BankQuestion | null;
  kind: QuestionKind;
  categories: string[];
}

@Component({
  selector: 'app-bank-question-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, QuestionFields, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'bank.kind.' + data.kind | translate }}</h2>
    <mat-dialog-content>
      <form id="bank-form" class="form" [formGroup]="question" (ngSubmit)="save()">
        @if (error(); as key) {
          <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
        }
        <mat-form-field>
          <mat-label>{{ 'bank.category' | translate }}</mat-label>
          <input matInput maxlength="100" list="bank-categories" [formControl]="category" />
          <datalist id="bank-categories">
            @for (name of data.categories; track name) {
              <option [value]="name"></option>
            }
          </datalist>
        </mat-form-field>
        <app-question-fields [group]="question" />
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="bank-form" [disabled]="busy()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .form {
      display: grid;
      gap: var(--space-2);
      padding-block-start: var(--space-2);
    }
  `,
})
export class BankQuestionDialog {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialogRef<BankQuestionDialog, boolean>);

  protected readonly data = inject<EditData>(MAT_DIALOG_DATA);
  protected readonly question: FormGroup = questionGroup(inject(FormBuilder), this.data.question ?? this.data.kind);
  protected readonly category = new FormControl(this.data.question?.category ?? '', { nonNullable: true });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async save(): Promise<void> {
    if (this.question.invalid || this.busy()) {
      this.question.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const url = bankUrl(this.data.courseId);
    const body = { category: this.category.value, question: toDraft(this.question) };

    try {
      await firstValueFrom(this.data.question ? this.http.put(`${url}/${this.data.question.id}`, body) : this.http.post(url, body));
      this.dialog.close(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'app-question-bank',
  imports: [FilterBar, MatButtonModule, MatFormFieldModule, MatIconModule, MatMenuModule, MatPaginatorModule, MatSelectModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton routerLink="/courses" class="back">
        <mat-icon class="flip-rtl">arrow_back</mat-icon>
        {{ 'nav.courses' | translate }}
      </a>

      <header class="page-header">
        <div>
          <h1>{{ 'bank.title' | translate }}</h1>
          <p>{{ 'bank.subtitle' | translate }}</p>
        </div>
        <div class="header-actions">
          <input #file type="file" accept=".xml" hidden (change)="import(file)" />
          <button matButton="outlined" [disabled]="busy()" (click)="file.click()">
            <mat-icon>upload_file</mat-icon>
            {{ 'bank.import' | translate }}
          </button>
          <button matButton="filled" [matMenuTriggerFor]="kinds">
            <mat-icon>add</mat-icon>
            {{ 'bank.new' | translate }}
          </button>
          <mat-menu #kinds="matMenu">
            @for (kind of kindList; track kind) {
              <button mat-menu-item (click)="open(null, kind)">{{ 'bank.kind.' + kind | translate }}</button>
            }
          </mat-menu>
        </div>
      </header>

      <app-filter-bar
        [label]="'bank.search' | translate"
        [search]="filters.values.search()"
        [count]="data()?.questions?.total ?? null"
        [active]="filters.activeCount()"
        (searchChange)="filters.set('search', $event)"
        (clear)="filters.clear()"
      >
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'bank.category' | translate }}</mat-label>
          <mat-select [value]="filters.values.category()" (selectionChange)="filters.set('category', $event.value)">
            <mat-option value="">{{ 'filters.allCategories' | translate }}</mat-option>
            @for (name of data()?.categories; track name) {
              <mat-option [value]="name">{{ name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'bank.type' | translate }}</mat-label>
          <mat-select [value]="filters.values.kind()" (selectionChange)="filters.set('kind', $event.value)">
            <mat-option value="">{{ 'filters.allTypes' | translate }}</mat-option>
            @for (kind of kindList; track kind) {
              <mat-option [value]="kind">{{ 'bank.kind.' + kind | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </app-filter-bar>

      <section class="panel">
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
                <span class="text">
                  <strong>{{ label(question) }}</strong>
                  <small>
                    {{ 'bank.kind.' + question.kind | translate }}
                    · {{ 'quiz.points' | translate: { points: question.points } }}
                    @if (question.category) {
                      · {{ question.category }}
                    }
                  </small>
                </span>
                <button matIconButton [attr.aria-label]="'common.edit' | translate" (click)="open(question, question.kind)"><mat-icon>edit</mat-icon></button>
                <button matIconButton [attr.aria-label]="'common.remove' | translate" (click)="remove(question)"><mat-icon>delete</mat-icon></button>
              </li>
            }
          </ul>
          <mat-paginator [length]="data()!.questions.total" [pageIndex]="filters.page()" [pageSize]="20" [hidePageSize]="true" (page)="onPage($event)" />
        }
      </section>
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    .text {
      display: grid;
      flex: 1;
      gap: 2px;
      min-width: 0;
    }
    strong {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class QuestionBank {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly languages = inject(LanguageService);

  readonly id = input.required<string>();

  protected readonly kindList = QUESTION_KINDS;
  protected readonly filters = urlFilters({ search: '', category: '', kind: '' });
  protected readonly busy = signal(false);
  protected readonly resource = httpResource<BankList>(() => ({
    url: bankUrl(this.id()),
    params: {
      page: this.filters.page() + 1,
      pageSize: 20,
      search: this.filters.values.search(),
      category: this.filters.values.category(),
      ...(this.filters.values.kind() ? { kind: this.filters.values.kind() } : {}),
    },
  }));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected label(question: BankQuestion): string {
    return this.languages.language() === 'ar' ? question.textAr : question.textEn;
  }

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
  }

  protected async open(question: BankQuestion | null, kind: QuestionKind): Promise<void> {
    const ref = this.dialog.open(BankQuestionDialog, {
      width: '52rem',
      maxWidth: 'calc(100vw - 2rem)',
      data: { courseId: this.id(), question, kind, categories: this.data()?.categories ?? [] },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      this.resource.reload();
    }
  }

  protected async remove(question: BankQuestion): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'bank.removeTitle', messageKey: 'bank.removeMessage', confirmKey: 'common.remove' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await firstValueFrom(this.http.delete(`${bankUrl(this.id())}/${question.id}`));
      this.resource.reload();
    }
  }

  protected async import(box: HTMLInputElement): Promise<void> {
    const file = box.files?.[0];
    box.value = '';

    if (!file) {
      return;
    }

    const form = new FormData();
    form.set('file', file);
    this.busy.set(true);

    try {
      const result = await firstValueFrom(
        this.http.post<{ imported: number; duplicates: number; unsupported: number }>(`${bankUrl(this.id())}/import`, form),
      );
      this.snackBar.open(this.translate.instant('bank.imported', result), undefined, { duration: 8000 });
      this.resource.reload();
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
    } finally {
      this.busy.set(false);
    }
  }
}

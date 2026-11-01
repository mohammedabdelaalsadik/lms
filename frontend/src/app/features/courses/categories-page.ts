import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

export const CATEGORIES_URL = '/api/v1/course-categories';

export interface Category {
  id: string;
  name: LocalizedText;
  parentId: string | null;
}

export interface CategoryRow extends Category {
  depth: number;
}

/** Parents first, each followed by its own children. */
export function asTree(all: Category[]): CategoryRow[] {
  const rows: CategoryRow[] = [];
  const walk = (parentId: string | null, depth: number): void => {
    for (const category of all.filter((c) => c.parentId === parentId)) {
      rows.push({ ...category, depth });
      walk(category.id, depth + 1);
    }
  };

  walk(null, 0);
  return rows;
}

interface EditData {
  category: Category | null;
  rows: CategoryRow[];
}

@Component({
  selector: 'app-category-dialog',
  imports: [LocalizedPipe, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (data.category ? 'categories.edit' : 'categories.new') | translate }}</h2>
    <mat-dialog-content>
      <form id="category-form" class="form" [formGroup]="form" (ngSubmit)="save()">
        @if (error(); as key) {
          <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
        }
        <mat-form-field>
          <mat-label>{{ 'fields.fullNameAr' | translate }}</mat-label>
          <input matInput dir="rtl" formControlName="nameAr" maxlength="150" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.fullNameEn' | translate }}</mat-label>
          <input matInput dir="ltr" formControlName="nameEn" maxlength="150" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'categories.parent' | translate }}</mat-label>
          <mat-select formControlName="parentId">
            <mat-option [value]="''">{{ 'categories.top' | translate }}</mat-option>
            @for (row of data.rows; track row.id) {
              @if (row.id !== data.category?.id) {
                <mat-option [value]="row.id">{{ row.name | localized }}</mat-option>
              }
            }
          </mat-select>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="category-form" [disabled]="busy()">{{ 'common.save' | translate }}</button>
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
export class CategoryDialog {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialogRef<CategoryDialog, boolean>);

  protected readonly data = inject<EditData>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    nameAr: [this.data.category?.name.ar ?? '', Validators.required],
    nameEn: [this.data.category?.name.en ?? '', Validators.required],
    parentId: [this.data.category?.parentId ?? ''],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const value = this.form.getRawValue();
    const body = { ...value, parentId: value.parentId || null };
    const id = this.data.category?.id;

    try {
      await firstValueFrom(id ? this.http.put(`${CATEGORIES_URL}/${id}`, body) : this.http.post(CATEGORIES_URL, body));
      this.dialog.close(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'app-categories-page',
  imports: [LocalizedPipe, MatButtonModule, MatIconModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'categories.title' | translate }}</h1>
          <p>{{ 'categories.subtitle' | translate }}</p>
        </div>
        <button matButton="filled" (click)="open(null)"><mat-icon>add</mat-icon>{{ 'categories.new' | translate }}</button>
      </header>

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (!rows()) {
          <app-state-panel kind="loading" />
        } @else if (rows()!.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (row of rows(); track row.id) {
              <li [style.padding-inline-start.rem]="1 + row.depth * 1.5">
                <mat-icon aria-hidden="true">{{ row.depth === 0 ? 'folder' : 'subdirectory_arrow_left' }}</mat-icon>
                <span>{{ row.name | localized }}</span>
                <button matIconButton [attr.aria-label]="'common.edit' | translate" (click)="open(row)"><mat-icon>edit</mat-icon></button>
                <button matIconButton [attr.aria-label]="'common.remove' | translate" (click)="remove(row)"><mat-icon>delete</mat-icon></button>
              </li>
            }
          </ul>
        }
      </section>
    </div>
  `,
  styles: `
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding-block: var(--space-1);
      padding-inline-end: var(--space-3);
      border-block-end: 1px solid var(--line);
    }
    span {
      flex: 1;
      min-width: 0;
    }
  `,
})
export class CategoriesPage {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  protected readonly resource = httpResource<Category[]>(() => CATEGORIES_URL);
  protected readonly rows = computed(() => (this.resource.hasValue() ? asTree(this.resource.value()) : null));

  protected async open(category: Category | null): Promise<void> {
    const ref = this.dialog.open(CategoryDialog, { width: '30rem', maxWidth: 'calc(100vw - 2rem)', data: { category, rows: this.rows() ?? [] } });

    if (await firstValueFrom(ref.afterClosed())) {
      this.resource.reload();
    }
  }

  protected async remove(category: Category): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'categories.removeTitle', messageKey: 'categories.removeMessage', confirmKey: 'common.remove' },
    });

    if (!(await firstValueFrom(ref.afterClosed()))) {
      return;
    }

    try {
      await firstValueFrom(this.http.delete(`${CATEGORIES_URL}/${category.id}`));
      this.resource.reload();
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
    }
  }
}

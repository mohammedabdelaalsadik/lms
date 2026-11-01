import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { Course, LocalizedText, Paged } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

export interface CourseRef {
  id: string;
  code: string;
  title: LocalizedText;
}

export interface Competency {
  id: string;
  code: string;
  name: LocalizedText;
  description: LocalizedText;
  courses: CourseRef[];
  isRetired: boolean;
}

export const COMPETENCIES_URL = '/api/v1/competencies';

@Component({
  selector: 'app-competencies-page',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, ReactiveFormsModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'competencies.title' | translate }}</h1>
          <p>{{ 'competencies.subtitle' | translate }}</p>
        </div>
      </header>

      @if (canManage()) {
        <form class="panel editor" [formGroup]="form" (ngSubmit)="save()">
          @if (error(); as key) {
            <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
          }
          <mat-form-field><mat-label>{{ 'competencies.code' | translate }}</mat-label><input matInput dir="ltr" maxlength="30" formControlName="code" /></mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'competencies.courses' | translate }}</mat-label>
            <mat-select formControlName="courseIds" multiple>
              @for (course of courses(); track course.id) {
                <mat-option [value]="course.id">{{ course.code }} · {{ course.title | localized }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field><mat-label>{{ 'competencies.nameAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="150" formControlName="nameAr" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'competencies.nameEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="150" formControlName="nameEn" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'competencies.descriptionAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="1000" formControlName="descriptionAr" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'competencies.descriptionEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="1000" formControlName="descriptionEn" /></mat-form-field>
          <div class="actions">
            <button matButton="filled" type="submit" [disabled]="busy()">{{ (editing() ? 'competencies.update' : 'competencies.create') | translate }}</button>
            @if (editing()) {
              <button matButton type="button" (click)="reset()">{{ 'common.cancel' | translate }}</button>
            }
          </div>
        </form>
      }

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        <section class="panel list">
          @for (item of items(); track item.id) {
            <article [class.retired]="item.isRetired">
              <div>
                <h2><span class="ltr-value code">{{ item.code }}</span> {{ item.name | localized }}</h2>
                <p>{{ item.description | localized }}</p>
                <small>
                  @for (course of item.courses; track course.id) {
                    <span class="ltr-value chip">{{ course.code }}</span>
                  } @empty {
                    {{ 'competencies.noCourses' | translate }}
                  }
                </small>
              </div>
              @if (item.isRetired) {
                <small>{{ 'competencies.retired' | translate }}</small>
              } @else if (canManage()) {
                <div class="row-actions">
                  <button matButton (click)="edit(item)">{{ 'common.edit' | translate }}</button>
                  <button matButton (click)="retire(item)">{{ 'competencies.retire' | translate }}</button>
                </div>
              }
            </article>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .editor {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
      gap: var(--space-2) var(--space-3);
      align-items: center;
      padding: var(--space-4);
    }
    .form-error,
    .actions {
      grid-column: 1 / -1;
    }
    .actions,
    .row-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .list article {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
    }
    .list article + article {
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
    .list article > div:first-child {
      flex: 1 1 18rem;
      min-width: 0;
    }
    .retired {
      opacity: 0.65;
    }
    h2,
    p {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-small);
    }
    p,
    small {
      color: var(--mat-sys-on-surface-variant);
    }
    .code {
      color: var(--brand-primary);
    }
    .chip {
      display: inline-block;
      margin-block-start: var(--space-1);
      margin-inline-end: var(--space-1);
      padding-inline: var(--space-2);
      border-radius: var(--radius-s);
      background: var(--mat-sys-surface-container);
    }
  `,
})
export class CompetenciesPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly editing = signal<string | null>(null);
  protected readonly resource = httpResource<Competency[]>(() => COMPETENCIES_URL);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  private readonly courseResource = httpResource<Paged<Course>>(() => ({ url: '/api/v1/courses', params: { pageSize: 100 } }));
  protected readonly courses = computed(() => (this.courseResource.hasValue() ? this.courseResource.value().items : []));
  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', Validators.required],
    courseIds: [[] as string[]],
    nameAr: ['', Validators.required],
    nameEn: ['', Validators.required],
    descriptionAr: ['', Validators.required],
    descriptionEn: ['', Validators.required],
  });

  protected edit(item: Competency): void {
    this.editing.set(item.id);
    this.error.set(null);
    this.form.setValue({
      code: item.code,
      courseIds: item.courses.map((c) => c.id),
      nameAr: item.name.ar,
      nameEn: item.name.en,
      descriptionAr: item.description.ar,
      descriptionEn: item.description.en,
    });
    this.form.controls.code.disable();
  }

  protected reset(): void {
    this.editing.set(null);
    this.form.reset();
    this.form.controls.code.enable();
  }

  protected async save(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const id = this.editing();
      const body = this.form.getRawValue();
      await firstValueFrom(id ? this.http.put(`${COMPETENCIES_URL}/${id}`, body) : this.http.post(COMPETENCIES_URL, body));
      this.reset();
      this.resource.reload();
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async retire(item: Competency): Promise<void> {
    await firstValueFrom(this.http.post(`${COMPETENCIES_URL}/${item.id}/retire`, {}));
    this.resource.reload();
  }
}

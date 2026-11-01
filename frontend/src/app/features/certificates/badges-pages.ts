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
import { errorKey } from '../../core/http/api-error';
import { Course, LocalizedText, Paged } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface Badge {
  id: string;
  courseId: string;
  courseCode: string;
  name: LocalizedText;
  description: LocalizedText;
  icon: string;
  isRetired: boolean;
  awarded: number;
}

interface MyBadge {
  id: string;
  name: LocalizedText;
  description: LocalizedText;
  icon: string;
  awardedAt: string;
}

const URL_BASE = '/api/v1/badges';
const ICONS = ['military_tech', 'workspace_premium', 'emoji_events', 'verified', 'star', 'school'];

const CARD_STYLES = `
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
    gap: var(--space-3);
  }
  .card {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: var(--space-1) var(--space-3);
    padding: var(--space-4);
  }
  .card mat-icon {
    grid-row: span 3;
    width: 2.5rem;
    height: 2.5rem;
    color: var(--brand-primary);
    font-size: 2.5rem;
  }
  .card p,
  .card h2 {
    margin: 0;
  }
  .card h2 {
    font: var(--mat-sys-title-medium);
  }
  .card small {
    color: var(--mat-sys-on-surface-variant);
  }
`;

@Component({
  selector: 'app-my-badges',
  imports: [AppDatePipe, LocalizedPipe, MatIconModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'badges.mine' | translate }}</h1>
          <p>{{ 'badges.mineSubtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        <div class="grid">
          @for (badge of items(); track badge.id) {
            <article class="panel card">
              <mat-icon aria-hidden="true">{{ badge.icon }}</mat-icon>
              <h2>{{ badge.name | localized }}</h2>
              <p>{{ badge.description | localized }}</p>
              <small>{{ badge.awardedAt | appDate }}</small>
            </article>
          }
        </div>
      }
    </div>
  `,
  styles: CARD_STYLES,
})
export class MyBadges {
  protected readonly resource = httpResource<MyBadge[]>(() => `${URL_BASE}/my`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
}

@Component({
  selector: 'app-manage-badges',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, ReactiveFormsModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'badges.title' | translate }}</h1>
          <p>{{ 'badges.subtitle' | translate }}</p>
        </div>
      </header>

      <form class="panel editor" [formGroup]="form" (ngSubmit)="save()">
        @if (error(); as key) {
          <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
        }
        <mat-form-field>
          <mat-label>{{ 'badges.course' | translate }}</mat-label>
          <mat-select formControlName="courseId">
            @for (course of courses(); track course.id) {
              <mat-option [value]="course.id">{{ course.code }} · {{ course.title | localized }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'badges.icon' | translate }}</mat-label>
          <mat-select formControlName="icon">
            @for (icon of icons; track icon) {
              <mat-option [value]="icon"><mat-icon>{{ icon }}</mat-icon></mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field><mat-label>{{ 'badges.nameAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="100" formControlName="nameAr" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'badges.nameEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="100" formControlName="nameEn" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'badges.descriptionAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="500" formControlName="descriptionAr" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'badges.descriptionEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="500" formControlName="descriptionEn" /></mat-form-field>
        <button matButton="filled" type="submit" [disabled]="busy()">{{ 'badges.create' | translate }}</button>
      </form>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (items()?.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        <div class="grid">
          @for (badge of items(); track badge.id) {
            <article class="panel card">
              <mat-icon aria-hidden="true">{{ badge.icon }}</mat-icon>
              <h2>{{ badge.name | localized }}</h2>
              <p>{{ badge.description | localized }}</p>
              <small>
                <span class="ltr-value">{{ badge.courseCode }}</span> · {{ 'badges.awarded' | translate: { count: badge.awarded } }}
                @if (badge.isRetired) {
                  · {{ 'badges.retired' | translate }}
                } @else {
                  <button matButton (click)="retire(badge)">{{ 'badges.retire' | translate }}</button>
                }
              </small>
            </article>
          }
        </div>
      }
    </div>
  `,
  styles:
    CARD_STYLES +
    `
    .editor {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
      gap: var(--space-2) var(--space-3);
      align-items: center;
      padding: var(--space-4);
    }
  `,
})
export class ManageBadges {
  private readonly http = inject(HttpClient);

  protected readonly icons = ICONS;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly resource = httpResource<Badge[]>(() => URL_BASE);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  private readonly courseResource = httpResource<Paged<Course>>(() => ({ url: '/api/v1/courses', params: { pageSize: 100 } }));
  protected readonly courses = computed(() => (this.courseResource.hasValue() ? this.courseResource.value().items : []));
  protected readonly form = inject(FormBuilder).nonNullable.group({
    courseId: ['', Validators.required],
    icon: [ICONS[0], Validators.required],
    nameAr: ['', Validators.required],
    nameEn: ['', Validators.required],
    descriptionAr: ['', Validators.required],
    descriptionEn: ['', Validators.required],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(this.http.post(URL_BASE, this.form.getRawValue()));
      this.form.reset();
      this.resource.reload();
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async retire(badge: Badge): Promise<void> {
    await firstValueFrom(this.http.post(`${URL_BASE}/${badge.id}/retire`, {}));
    this.resource.reload();
  }
}

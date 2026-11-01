import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { COMPETENCIES_URL, Competency } from './competencies-page';

interface Plan {
  id: string;
  name: LocalizedText;
  description: LocalizedText;
  competencyIds: string[];
  assigned: number;
  isRetired: boolean;
}

export const PLANS_URL = '/api/v1/learning-plans';

@Component({
  selector: 'app-plans-page',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, ReactiveFormsModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'plans.title' | translate }}</h1>
          <p>{{ 'plans.subtitle' | translate }}</p>
        </div>
      </header>

      @if (canManage()) {
        <form class="panel editor" [formGroup]="form" (ngSubmit)="save()">
          @if (error(); as key) {
            <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
          }
          <mat-form-field><mat-label>{{ 'plans.nameAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="150" formControlName="nameAr" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'plans.nameEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="150" formControlName="nameEn" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'plans.descriptionAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="1000" formControlName="descriptionAr" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'plans.descriptionEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="1000" formControlName="descriptionEn" /></mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'plans.competencies' | translate }}</mat-label>
            <mat-select formControlName="competencyIds" multiple>
              @for (item of competencies(); track item.id) {
                <mat-option [value]="item.id">{{ item.code }} · {{ item.name | localized }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <div class="actions">
            <button matButton="filled" type="submit" [disabled]="busy()">{{ (editing() ? 'competencies.update' : 'plans.create') | translate }}</button>
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
          @for (plan of items(); track plan.id) {
            <article [class.retired]="plan.isRetired">
              <div>
                <h2><a [routerLink]="['/plans', plan.id]">{{ plan.name | localized }}</a></h2>
                <p>{{ plan.description | localized }}</p>
                <small>{{ 'plans.assigned' | translate: { count: plan.assigned } }} · {{ plan.competencyIds.length }} {{ 'plans.competencies' | translate }}</small>
              </div>
              @if (plan.isRetired) {
                <small>{{ 'plans.retired' | translate }}</small>
              } @else if (canManage()) {
                <div class="actions">
                  <button matButton (click)="edit(plan)">{{ 'common.edit' | translate }}</button>
                  <button matButton (click)="retire(plan)">{{ 'plans.retire' | translate }}</button>
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
    .form-error {
      grid-column: 1 / -1;
    }
    .actions {
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
    .list article > div {
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
  `,
})
export class PlansPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly busy = signal(false);
  protected readonly editing = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly resource = httpResource<Plan[]>(() => PLANS_URL);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  private readonly competencyResource = httpResource<Competency[]>(() => COMPETENCIES_URL);
  protected readonly competencies = computed(() => (this.competencyResource.hasValue() ? this.competencyResource.value().filter((c) => !c.isRetired || this.form.getRawValue().competencyIds.includes(c.id)) : []));
  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly form = inject(FormBuilder).nonNullable.group({
    nameAr: ['', Validators.required],
    nameEn: ['', Validators.required],
    descriptionAr: ['', Validators.required],
    descriptionEn: ['', Validators.required],
    competencyIds: [[] as string[], Validators.required],
  });

  protected edit(plan: Plan): void {
    this.editing.set(plan.id);
    this.error.set(null);
    this.form.setValue({
      nameAr: plan.name.ar,
      nameEn: plan.name.en,
      descriptionAr: plan.description.ar,
      descriptionEn: plan.description.en,
      competencyIds: plan.competencyIds,
    });
  }

  protected reset(): void {
    this.editing.set(null);
    this.form.reset();
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
      await firstValueFrom(id ? this.http.put(`${PLANS_URL}/${id}`, body) : this.http.post(PLANS_URL, body));
      this.reset();
      this.resource.reload();
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async retire(plan: Plan): Promise<void> {
    await firstValueFrom(this.http.post(`${PLANS_URL}/${plan.id}/retire`, {}));
    this.resource.reload();
  }
}

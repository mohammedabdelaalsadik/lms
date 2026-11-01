import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { ChoiceChips } from '../../shared/choice-chips';
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { FilterBar } from '../../shared/filter-bar';
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
  imports: [
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    ReactiveFormsModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'plans.title' | translate }}</h1>
          <p>{{ 'plans.subtitle' | translate }}</p>
        </div>
        @if (canManage() && !showForm()) {
          <button matButton="filled" (click)="showForm.set(true)"><mat-icon>add</mat-icon>{{ 'plans.create' | translate }}</button>
        }
      </header>

      @if (showForm()) {
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
            <button matButton type="button" (click)="reset()">{{ 'common.cancel' | translate }}</button>
          </div>
        </form>
      }

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <app-filter-bar
          [label]="'plans.search' | translate"
          [search]="search()"
          [count]="shown().length"
          [active]="(search() ? 1 : 0) + (state() ? 1 : 0)"
          (searchChange)="find($event)"
          (clear)="clearFilters()"
        >
          <app-choice-chips [label]="'courses.status' | translate" [choices]="stateChoices" [value]="state()" (valueChange)="setState($event)" />
        </app-filter-bar>

        @if (shown().length === 0) {
          <section class="panel"><app-state-panel kind="empty" /></section>
        } @else {
          <section class="grid">
            @for (plan of visible(); track plan.id) {
              <article class="panel plan" [class.retired]="plan.isRetired">
                <div class="top">
                  <span class="icon" aria-hidden="true"><mat-icon>route</mat-icon></span>
                  <div class="title">
                    <h2><a [routerLink]="['/plans', plan.id]">{{ plan.name | localized }}</a></h2>
                    @if (plan.isRetired) {
                      <span class="state">{{ 'plans.retired' | translate }}</span>
                    }
                  </div>
                </div>
                <p>{{ plan.description | localized }}</p>
                <ul class="facts">
                  <li><mat-icon aria-hidden="true">group</mat-icon>{{ 'plans.traineesCount' | translate: { count: plan.assigned } }}</li>
                  <li><mat-icon aria-hidden="true">psychology</mat-icon>{{ 'plans.competenciesCount' | translate: { count: plan.competencyIds.length } }}</li>
                </ul>
                <div class="actions">
                  <a matButton="outlined" [routerLink]="['/plans', plan.id]">{{ 'plans.open' | translate }}</a>
                  @if (canManage() && !plan.isRetired) {
                    <button matButton (click)="edit(plan)">{{ 'common.edit' | translate }}</button>
                    <button matButton (click)="retire(plan)">{{ 'plans.retire' | translate }}</button>
                  }
                </div>
              </article>
            }
          </section>

          @if (shown().length > pageSize()) {
            <section class="panel">
              <mat-paginator
                [length]="shown().length"
                [pageIndex]="page()"
                [pageSize]="pageSize()"
                [pageSizeOptions]="[12, 24, 48]"
                (page)="onPage($event)"
              />
            </section>
          }
        }
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
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(19rem, 1fr));
      gap: var(--space-4);
    }
    .plan {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      padding: var(--space-4);
      transition:
        border-color 0.15s ease,
        transform 0.15s ease;

      &:hover {
        border-color: var(--brand-primary);
        transform: translateY(-1px);
      }
    }
    .top {
      display: flex;
      align-items: center;
      gap: var(--space-3);
    }
    .icon {
      display: grid;
      flex: none;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border-radius: var(--radius-s);
      background: var(--tint-blue);
      color: var(--on-tint-blue);
    }
    .title {
      min-width: 0;
    }
    h2,
    p {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-medium);
      font-weight: 600;

      a {
        color: inherit;
        text-decoration: none;

        &:hover {
          color: var(--brand-link);
          text-decoration: underline;
        }
      }
    }
    .plan p {
      display: -webkit-box;
      flex: 1;
      overflow: hidden;
      color: var(--mat-sys-on-surface-variant);
      -webkit-box-orient: vertical;
      -webkit-line-clamp: 2;
    }
    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;

      li {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        padding: 0.125rem var(--space-2);
        border-radius: 999px;
        background: var(--mat-sys-surface-container);
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-medium);
      }

      mat-icon {
        width: 16px;
        height: 16px;
        font-size: 16px;
      }
    }
    .state {
      padding: 0.0625rem var(--space-2);
      border-radius: 999px;
      background: var(--tint-grey);
      color: var(--on-tint-grey);
      font: var(--mat-sys-label-small);
    }
    .retired {
      background: var(--mat-sys-surface-container-low);
    }
    .retired .icon {
      background: var(--tint-grey);
      color: var(--on-tint-grey);
    }
  `,
})
export class PlansPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  protected readonly busy = signal(false);
  protected readonly showForm = signal(false);
  protected readonly search = signal('');
  protected readonly state = signal('');
  protected readonly page = signal(0);
  protected readonly pageSize = signal(12);
  protected readonly stateChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'active', labelKey: 'plans.active' },
    { value: 'retired', labelKey: 'plans.retired' },
  ];
  protected readonly editing = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly resource = httpResource<Plan[]>(() => PLANS_URL);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  private readonly competencyResource = httpResource<Competency[]>(() => COMPETENCIES_URL);
  protected readonly competencies = computed(() => (this.competencyResource.hasValue() ? this.competencyResource.value().filter((c) => !c.isRetired || this.form.getRawValue().competencyIds.includes(c.id)) : []));
  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();

    return (this.items() ?? []).filter(
      (plan) =>
        (!this.state() || plan.isRetired === (this.state() === 'retired')) &&
        (!term || plan.name.ar.toLowerCase().includes(term) || plan.name.en.toLowerCase().includes(term)),
    );
  });
  protected readonly visible = computed(() => {
    const start = this.page() * this.pageSize();
    return this.shown().slice(start, start + this.pageSize());
  });
  protected readonly form = inject(FormBuilder).nonNullable.group({
    nameAr: ['', Validators.required],
    nameEn: ['', Validators.required],
    descriptionAr: ['', Validators.required],
    descriptionEn: ['', Validators.required],
    competencyIds: [[] as string[], Validators.required],
  });

  protected find(term: string): void {
    this.search.set(term);
    this.page.set(0);
  }

  protected setState(value: string): void {
    this.state.set(value);
    this.page.set(0);
  }

  protected clearFilters(): void {
    this.state.set('');
    this.find('');
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  protected edit(plan: Plan): void {
    this.showForm.set(true);
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
    this.showForm.set(false);
    this.editing.set(null);
    this.error.set(null);
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
    const data: ConfirmData = { titleKey: 'plans.retireTitle', messageKey: 'plans.retireMessage', confirmKey: 'plans.retire' };
    const confirmed = await firstValueFrom(this.dialog.open(ConfirmDialog, { data, width: '26rem', maxWidth: 'calc(100vw - 2rem)' }).afterClosed());

    if (!confirmed) {
      return;
    }

    await firstValueFrom(this.http.post(`${PLANS_URL}/${plan.id}/retire`, {}));
    this.resource.reload();
  }
}

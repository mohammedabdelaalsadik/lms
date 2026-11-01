import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { Cohort, LocalizedText, Paged } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { DateField } from '../../shared/date-field';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { COMPETENCIES_URL } from './competencies-page';
import { PLANS_URL } from './plans-page';

type Source = 'None' | 'Course' | 'SignOff';

interface Column {
  id: string;
  code: string;
  name: LocalizedText;
}

interface Row {
  traineeId: string;
  name: LocalizedText;
  dueOn: string | null;
  cells: Record<string, Source>;
  met: number;
}

interface Progress {
  id: string;
  name: LocalizedText;
  description: LocalizedText;
  isRetired: boolean;
  competencies: Column[];
  trainees: Row[];
}

@Component({
  selector: 'app-plan-detail',
  imports: [
    AppDatePipe,
    DateField,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule,
    MatTooltipModule,
    ReactiveFormsModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <div class="page">
      <a matButton routerLink="/plans" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'plans.title' | translate }}</a>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <header class="page-header">
          <div>
            <h1>{{ data()!.name | localized }}</h1>
            <p>{{ data()!.description | localized }}</p>
          </div>
        </header>

        @if (canManage() && !data()!.isRetired) {
          <form class="panel assign" [formGroup]="form" (ngSubmit)="assign()">
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'plans.cohort' | translate }}</mat-label>
              <mat-select formControlName="cohortId">
                @for (cohort of cohorts(); track cohort.id) {
                  <mat-option [value]="cohort.id">{{ cohort.name | localized }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'plans.oneTrainee' | translate }}</mat-label>
              <mat-select formControlName="traineeId">
                <mat-option value="">{{ 'plans.wholeCohort' | translate }}</mat-option>
                @for (person of roster(); track person.traineeId) {
                  <mat-option [value]="person.traineeId">{{ person.traineeName | localized }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <app-date-field formControlName="dueOn" [clearable]="true" [label]="'plans.dueOn' | translate" />
            <button matButton="filled" type="submit" [disabled]="busy()">{{ (form.controls.traineeId.value ? 'plans.assignOne' : 'plans.assign') | translate }}</button>
          </form>
        }

        <section class="panel">
          @if (data()!.trainees.length === 0) {
            <p class="none">{{ 'plans.noTrainees' | translate }}</p>
          } @else {
            <div class="scroll" tabindex="0">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{{ 'plans.trainee' | translate }}</th>
                    @for (column of data()!.competencies; track column.id) {
                      <th scope="col" [matTooltip]="column.name | localized"><span class="ltr-value">{{ column.code }}</span></th>
                    }
                    <th scope="col">{{ 'plans.competencies' | translate }}</th>
                    @if (canManage()) {
                      <th scope="col"><span class="visually-hidden">{{ 'plans.removeTrainee' | translate }}</span></th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of data()!.trainees; track row.traineeId) {
                    <tr>
                      <th scope="row">
                        {{ row.name | localized }}
                        @if (row.dueOn) {
                          <small>{{ 'plans.due' | translate: { date: row.dueOn | appDate } }}</small>
                        }
                      </th>
                      @for (column of data()!.competencies; track column.id) {
                        <td>
                          @switch (row.cells[column.id]) {
                            @case ('Course') {
                              <mat-icon class="met" [matTooltip]="'plans.viaCourse' | translate" [attr.aria-label]="'plans.viaCourse' | translate">verified</mat-icon>
                            }
                            @case ('SignOff') {
                              @if (canManage()) {
                                <button matIconButton class="met" [matTooltip]="'plans.clearSignOff' | translate" [attr.aria-label]="'plans.clearSignOff' | translate" [disabled]="busy()" (click)="toggle(column, row, false)">
                                  <mat-icon>how_to_reg</mat-icon>
                                </button>
                              } @else {
                                <mat-icon class="met" [matTooltip]="'plans.viaSignOff' | translate" [attr.aria-label]="'plans.viaSignOff' | translate">how_to_reg</mat-icon>
                              }
                            }
                            @default {
                              @if (canManage()) {
                                <button matIconButton [matTooltip]="'plans.signOff' | translate" [attr.aria-label]="'plans.signOff' | translate" [disabled]="busy()" (click)="toggle(column, row, true)">
                                  <mat-icon>radio_button_unchecked</mat-icon>
                                </button>
                              } @else {
                                <mat-icon class="unmet" [attr.aria-label]="'plans.notMet' | translate">radio_button_unchecked</mat-icon>
                              }
                            }
                          }
                        </td>
                      }
                      <td class="count">{{ 'plans.progress' | translate: { met: row.met, total: data()!.competencies.length } }}</td>
                      @if (canManage()) {
                        <td>
                          <button matIconButton [attr.aria-label]="'plans.removeTrainee' | translate" [matTooltip]="'plans.removeTrainee' | translate" (click)="unassign(row)"><mat-icon>person_remove</mat-icon></button>
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .assign {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-4);

      mat-form-field,
      app-date-field {
        flex: 1 1 14rem;
      }
    }
    .none {
      margin: 0;
      padding: var(--space-6) var(--space-4);
      color: var(--mat-sys-on-surface-variant);
    }
    .scroll {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th,
    td {
      padding: var(--space-1) var(--space-3);
      border-block-end: 1px solid var(--mat-sys-outline-variant);
      text-align: center;
      white-space: nowrap;
    }
    th:first-child {
      text-align: start;
      font-weight: 500;
    }
    tbody th small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
      font-weight: 400;
    }
    .met {
      color: var(--brand-primary);
    }
    .unmet {
      color: var(--mat-sys-outline);
    }
    .count {
      color: var(--mat-sys-on-surface-variant);
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class PlanDetail {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<Progress>(() => `${PLANS_URL}/${this.id()}`);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  private readonly cohortResource = httpResource<Paged<Cohort>>(() => ({ url: '/api/v1/cohorts', params: { pageSize: 100 } }));
  protected readonly cohorts = computed(() => (this.cohortResource.hasValue() ? this.cohortResource.value().items : []));
  private readonly chosenCohort = signal('');
  private readonly rosterResource = httpResource<{ entries: { traineeId: string; traineeName: LocalizedText; status: string }[] }>(() =>
    this.chosenCohort() ? `/api/v1/cohorts/${this.chosenCohort()}/roster` : undefined,
  );
  protected readonly roster = computed(() => (this.rosterResource.hasValue() ? this.rosterResource.value().entries.filter((e) => e.status === 'Enrolled') : []));
  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly form = inject(FormBuilder).group({
    cohortId: ['', Validators.required],
    traineeId: [''],
    dueOn: [null as string | null],
  });

  constructor() {
    this.form.controls.cohortId.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.chosenCohort.set(value ?? '');
      this.form.controls.traineeId.setValue('');
    });
  }

  protected async assign(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { cohortId, traineeId, dueOn } = this.form.getRawValue();
    const count = await this.run(
      traineeId
        ? this.http.post<number>(`${PLANS_URL}/${this.id()}/trainees/${traineeId}`, { dueOn })
        : this.http.post<number>(`${PLANS_URL}/${this.id()}/assign`, { cohortId, dueOn }),
    );
    if (count !== null) {
      this.snackBar.open(this.translate.instant('plans.added', { count }), undefined, { duration: 4000 });
      this.form.reset();
    }
  }

  protected toggle(column: Column, row: Row, met: boolean): Promise<unknown> {
    const url = `${COMPETENCIES_URL}/${column.id}/sign-offs/${row.traineeId}`;
    return this.run(met ? this.http.put(url, {}) : this.http.delete(url));
  }

  protected async unassign(row: Row): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'plans.removeTitle', messageKey: 'plans.removeMessage', confirmKey: 'common.remove' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.http.delete(`${PLANS_URL}/${this.id()}/trainees/${row.traineeId}`));
    }
  }

  private async run<T>(call: Observable<T>): Promise<T | true | null> {
    this.busy.set(true);

    try {
      const value = await firstValueFrom(call);
      this.resource.reload();
      return value ?? true;
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}

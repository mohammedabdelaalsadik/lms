import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { downloadFile } from '../../core/http/download';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ChoiceChips } from '../../shared/choice-chips';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface Row {
  cohortId: string;
  courseCode: string;
  course: LocalizedText;
  cohort: LocalizedText;
  instructor: LocalizedText;
  startsOn: string;
  endsOn: string;
  enrolled: number;
  completed: number;
  completionPercent: number;
  averageGrade: number | null;
  certificates: number;
}

interface Overview {
  cohorts: number;
  enrolled: number;
  completed: number;
  certificates: number;
  rows: Row[];
}

const URL_BASE = '/api/v1/reports/overview';

@Component({
  selector: 'app-reports-page',
  imports: [
    AppDatePipe,
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatSelectModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'reports.title' | translate }}</h1>
          <p>{{ 'reports.subtitle' | translate }}</p>
        </div>
        <button matButton="filled" (click)="export()"><mat-icon>download</mat-icon>{{ 'reports.export' | translate }}</button>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <dl class="totals">
          <div class="total blue">
            <span class="icon"><mat-icon aria-hidden="true">groups</mat-icon></span>
            <dt>{{ 'reports.cohorts' | translate }}</dt>
            <dd>{{ data()!.cohorts }}</dd>
          </div>
          <div class="total plum">
            <span class="icon"><mat-icon aria-hidden="true">person_add</mat-icon></span>
            <dt>{{ 'reports.enrolled' | translate }}</dt>
            <dd>{{ data()!.enrolled }}</dd>
          </div>
          <div class="total green">
            <span class="icon"><mat-icon aria-hidden="true">task_alt</mat-icon></span>
            <dt>{{ 'reports.completed' | translate }}</dt>
            <dd>{{ data()!.completed }}</dd>
            <small>{{ 'reports.completionRate' | translate: { percent: completionRate() } }}</small>
          </div>
          <div class="total amber">
            <span class="icon"><mat-icon aria-hidden="true">workspace_premium</mat-icon></span>
            <dt>{{ 'reports.certificates' | translate }}</dt>
            <dd>{{ data()!.certificates }}</dd>
          </div>
        </dl>

        <app-filter-bar
          [label]="'reports.search' | translate"
          [search]="search()"
          [count]="rows().length"
          [active]="(search() ? 1 : 0) + (period() ? 1 : 0) + (instructor() ? 1 : 0)"
          (searchChange)="find($event)"
          (clear)="clearFilters()"
        >
          <app-choice-chips [label]="'filters.period' | translate" [choices]="periodChoices" [value]="period()" (valueChange)="setPeriod($event)" />
          <mat-form-field subscriptSizing="dynamic" class="instructor">
            <mat-label>{{ 'filters.instructor' | translate }}</mat-label>
            <mat-select [value]="instructor()" (selectionChange)="setInstructor($event.value)">
              <mat-option value="">{{ 'filters.allInstructors' | translate }}</mat-option>
              @for (name of instructors(); track $index) {
                <mat-option [value]="name.en">{{ name | localized }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </app-filter-bar>

        <section class="panel">
          @if (rows().length === 0) {
            <app-state-panel kind="empty" />
          } @else {
            <table>
              <thead>
                <tr>
                  <th>{{ 'reports.course' | translate }}</th>
                  <th>{{ 'reports.cohort' | translate }}</th>
                  <th>{{ 'reports.dates' | translate }}</th>
                  <th class="num">{{ 'reports.enrolled' | translate }}</th>
                  <th>{{ 'reports.completion' | translate }}</th>
                  <th class="num">{{ 'reports.average' | translate }}</th>
                  <th class="num">{{ 'reports.certificates' | translate }}</th>
                </tr>
              </thead>
              <tbody>
                @for (row of visible(); track row.cohortId) {
                  <tr>
                    <td class="course">
                      <strong>{{ row.course | localized }}</strong>
                      <span class="ltr-value code">{{ row.courseCode }}</span>
                    </td>
                    <td [attr.data-label]="'reports.cohort' | translate">
                      <span>
                        <a [routerLink]="['/cohorts', row.cohortId]">{{ row.cohort | localized }}</a>
                        <small>{{ row.instructor | localized }}</small>
                      </span>
                    </td>
                    <td class="dates" [attr.data-label]="'reports.dates' | translate">
                      <span>
                        {{ row.startsOn | appDate }}
                        <small>{{ row.endsOn | appDate }}</small>
                      </span>
                    </td>
                    <td class="num" [attr.data-label]="'reports.enrolled' | translate">{{ row.enrolled }}</td>
                    <td class="progress" [attr.data-label]="'reports.completion' | translate">
                      <div class="meter">
                        <span
                          class="track"
                          role="progressbar"
                          aria-valuemin="0"
                          aria-valuemax="100"
                          [attr.aria-valuenow]="row.completionPercent"
                          [attr.aria-label]="'reports.completion' | translate"
                        >
                          <span class="fill" [style.inline-size.%]="row.completionPercent"></span>
                        </span>
                        <span class="percent">{{ row.completionPercent }}%</span>
                      </div>
                    </td>
                    <td class="num" [attr.data-label]="'reports.average' | translate">
                      @if (row.averageGrade !== null) {
                        <span class="grade" [class]="gradeBand(row.averageGrade)">{{ row.averageGrade }}%</span>
                      } @else {
                        <small>{{ 'reports.noGrade' | translate }}</small>
                      }
                    </td>
                    <td class="num" [attr.data-label]="'reports.certificates' | translate">{{ row.certificates }}</td>
                  </tr>
                }
              </tbody>
            </table>

            <mat-paginator
              [length]="rows().length"
              [pageIndex]="page()"
              [pageSize]="pageSize()"
              [pageSizeOptions]="[10, 20, 50]"
              (page)="onPage($event)"
            />
          }
        </section>
      }
    </div>
  `,
  styles: `
    .totals {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--space-3);
      margin: 0;
    }
    .total {
      display: grid;
      grid-template-columns: auto 1fr;
      column-gap: var(--space-3);
      align-content: start;
      align-items: center;
      padding: var(--space-4);
      border: 1px solid var(--line);
      border-radius: var(--radius-m);
      background: var(--mat-sys-surface);
      box-shadow: var(--shadow-card);

      dt {
        grid-column: 2;
        grid-row: 1;
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-large);
      }

      dd {
        grid-column: 2;
        grid-row: 2;
        margin: 0;
        font: var(--mat-sys-headline-medium);
        font-weight: 700;
        font-variant-numeric: tabular-nums;
      }

      small {
        grid-column: 1 / -1;
        margin-block-start: var(--space-2);
        color: var(--mat-sys-on-surface-variant);
      }
    }
    .icon {
      display: grid;
      grid-row: 1 / span 2;
      place-items: center;
      width: 3rem;
      height: 3rem;
      border-radius: var(--radius-s);
      background: var(--tint);
      color: var(--on-tint);
    }
    .instructor {
      flex: 0 1 15rem;
    }
    .blue {
      --tint: var(--tint-blue);
      --on-tint: var(--on-tint-blue);
    }
    .plum {
      --tint: var(--tint-plum);
      --on-tint: var(--on-tint-plum);
    }
    .green {
      --tint: var(--tint-green);
      --on-tint: var(--on-tint-green);
    }
    .amber {
      --tint: var(--tint-amber);
      --on-tint: var(--on-tint-amber);
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      padding: var(--space-3) var(--space-4);
      background: color-mix(in srgb, var(--brand-primary) 7%, var(--mat-sys-surface));
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-large);
      font-weight: 600;
      text-align: start;
      white-space: nowrap;
    }
    td {
      padding: var(--space-3) var(--space-4);
      border-block-start: 1px solid var(--line);
      vertical-align: middle;
    }
    tbody tr {
      transition: background-color 0.15s ease;

      &:hover {
        background: color-mix(in srgb, var(--brand-primary) 5%, var(--mat-sys-surface));
      }
    }
    th.num,
    td.num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }
    td small,
    .code {
      display: block;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .course strong {
      display: block;
      font-weight: 600;
    }
    .code {
      direction: ltr;
      width: fit-content;
    }
    .dates {
      white-space: nowrap;
    }
    a {
      color: var(--brand-link);
      font-weight: 500;
      text-decoration: none;

      &:hover {
        text-decoration: underline;
      }
    }
    .progress {
      min-width: 9rem;
    }
    .meter {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }
    .track {
      flex: 1;
      height: 0.5rem;
      overflow: hidden;
      border-radius: 999px;
      background: var(--mat-sys-surface-container-high);
    }
    .fill {
      display: block;
      height: 100%;
      border-radius: inherit;
      background: var(--brand-primary);
    }
    .percent {
      min-width: 2.75rem;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      text-align: end;
    }
    .grade {
      display: inline-block;
      padding: 0.125rem var(--space-2);
      border-radius: 999px;
      font-weight: 600;

      &.high {
        background: var(--tint-green);
        color: var(--on-tint-green);
      }

      &.middle {
        background: var(--tint-amber);
        color: var(--on-tint-amber);
      }

      &.low {
        background: var(--mat-sys-error-container);
        color: var(--mat-sys-on-error-container);
      }
    }

    // On a phone each cohort becomes a card, so nothing scrolls sideways.
    @media (max-width: 56rem) {
      thead {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
      }
      table,
      tbody,
      tr,
      td {
        display: block;
      }
      tr {
        padding: var(--space-3) var(--space-4);
        border-block-start: 1px solid var(--line);
      }
      tbody tr:first-child {
        border-block-start: 0;
      }
      td {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-4);
        padding: var(--space-1) 0;
        border: 0;
        text-align: end;
      }
      td[data-label]::before {
        content: attr(data-label);
        flex: none;
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
      }
      td.course {
        display: block;
        padding-block-end: var(--space-2);
        text-align: start;
      }
      td.progress .meter {
        flex: 1;
        max-width: 12rem;
      }
    }
  `,
})
export class ReportsPage {
  private readonly http = inject(HttpClient);

  protected readonly search = signal('');
  protected readonly page = signal(0);
  protected readonly pageSize = signal(10);
  protected readonly resource = httpResource<Overview>(() => URL_BASE);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly period = signal('');
  protected readonly instructor = signal('');
  protected readonly periodChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'upcoming', labelKey: 'filters.upcoming' },
    { value: 'running', labelKey: 'filters.running' },
    { value: 'ended', labelKey: 'filters.ended' },
  ];
  protected readonly instructors = computed(() => {
    const seen = new Map<string, LocalizedText>();
    for (const row of this.data()?.rows ?? []) {
      seen.set(row.instructor.en, row.instructor);
    }
    return [...seen.values()];
  });
  protected readonly rows = computed(() => {
    const term = this.search().trim().toLowerCase();
    const today = new Date().toISOString().slice(0, 10);

    return (this.data()?.rows ?? []).filter((r) => {
      const inPeriod =
        !this.period() ||
        (this.period() === 'upcoming' && r.startsOn > today) ||
        (this.period() === 'running' && r.startsOn <= today && r.endsOn >= today) ||
        (this.period() === 'ended' && r.endsOn < today);
      const matches = !term || [r.courseCode, r.course.ar, r.course.en, r.cohort.ar, r.cohort.en, r.instructor.ar, r.instructor.en].some((t) => t.toLowerCase().includes(term));

      return inPeriod && matches && (!this.instructor() || r.instructor.en === this.instructor());
    });
  });
  protected readonly visible = computed(() => {
    const start = this.page() * this.pageSize();
    return this.rows().slice(start, start + this.pageSize());
  });
  protected readonly completionRate = computed(() => {
    const data = this.data();
    return data?.enrolled ? Math.round((data.completed / data.enrolled) * 100) : 0;
  });

  protected find(term: string): void {
    this.search.set(term);
    this.page.set(0);
  }

  protected setPeriod(value: string): void {
    this.period.set(value);
    this.page.set(0);
  }

  protected setInstructor(value: string): void {
    this.instructor.set(value);
    this.page.set(0);
  }

  protected clearFilters(): void {
    this.period.set('');
    this.instructor.set('');
    this.find('');
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  protected gradeBand(grade: number): 'high' | 'middle' | 'low' {
    return grade >= 80 ? 'high' : grade >= 60 ? 'middle' : 'low';
  }

  protected export(): Promise<void> {
    return downloadFile(this.http, `${URL_BASE}/export`, 'training-overview.csv');
  }
}

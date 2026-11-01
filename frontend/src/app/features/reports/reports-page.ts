import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { downloadFile } from '../../core/http/download';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
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
  imports: [AppDatePipe, FilterBar, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'reports.title' | translate }}</h1>
          <p>{{ 'reports.subtitle' | translate }}</p>
        </div>
        <button matButton="outlined" (click)="export()"><mat-icon>download</mat-icon>{{ 'reports.export' | translate }}</button>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <dl class="totals">
          <div><dt>{{ 'reports.cohorts' | translate }}</dt><dd>{{ data()!.cohorts }}</dd></div>
          <div><dt>{{ 'reports.enrolled' | translate }}</dt><dd>{{ data()!.enrolled }}</dd></div>
          <div><dt>{{ 'reports.completed' | translate }}</dt><dd>{{ data()!.completed }}</dd></div>
          <div><dt>{{ 'reports.certificates' | translate }}</dt><dd>{{ data()!.certificates }}</dd></div>
        </dl>

        <app-filter-bar
          [label]="'reports.search' | translate"
          [search]="search()"
          [count]="rows().length"
          [active]="search() ? 1 : 0"
          (searchChange)="search.set($event)"
          (clear)="search.set('')"
        />

        <section class="panel">
          @if (rows().length === 0) {
            <app-state-panel kind="empty" />
          } @else {
            <div class="scroll">
              <table>
                <thead>
                  <tr>
                    <th>{{ 'reports.course' | translate }}</th>
                    <th>{{ 'reports.cohort' | translate }}</th>
                    <th>{{ 'reports.instructor' | translate }}</th>
                    <th>{{ 'reports.dates' | translate }}</th>
                    <th class="num">{{ 'reports.enrolled' | translate }}</th>
                    <th class="num">{{ 'reports.completion' | translate }}</th>
                    <th class="num">{{ 'reports.average' | translate }}</th>
                    <th class="num">{{ 'reports.certificates' | translate }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of rows(); track row.cohortId) {
                    <tr>
                      <td><span class="ltr-value code">{{ row.courseCode }}</span> {{ row.course | localized }}</td>
                      <td><a [routerLink]="['/cohorts', row.cohortId]">{{ row.cohort | localized }}</a></td>
                      <td>{{ row.instructor | localized }}</td>
                      <td>{{ row.startsOn | appDate }} – {{ row.endsOn | appDate }}</td>
                      <td class="num">{{ row.enrolled }}</td>
                      <td class="num">{{ row.completionPercent }}%</td>
                      <td class="num">{{ row.averageGrade ?? '—' }}</td>
                      <td class="num">{{ row.certificates }}</td>
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
    .totals {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: var(--space-3);
      margin: 0;

      div {
        padding: var(--space-3) var(--space-4);
        border: 1px solid var(--line);
        border-radius: var(--radius-m);
        background: var(--mat-sys-surface);
      }

      dt {
        color: var(--mat-sys-on-surface-variant);
      }

      dd {
        margin: 0;
        font: var(--mat-sys-headline-small);
      }
    }
    .scroll {
      overflow-x: auto;
    }
    table {
      width: 100%;
      min-width: 56rem;
      border-collapse: collapse;
    }
    th,
    td {
      padding: var(--space-2) var(--space-3);
      border-block-end: 1px solid var(--line);
      text-align: start;
    }
    .num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }
    .code {
      color: var(--mat-sys-on-surface-variant);
    }
    a {
      color: var(--brand-link);
    }
  `,
})
export class ReportsPage {
  private readonly http = inject(HttpClient);

  protected readonly search = signal('');
  protected readonly resource = httpResource<Overview>(() => URL_BASE);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly rows = computed(() => {
    const term = this.search().trim().toLowerCase();
    const rows = this.data()?.rows ?? [];

    return term
      ? rows.filter((r) => [r.courseCode, r.course.ar, r.course.en, r.cohort.ar, r.cohort.en, r.instructor.ar, r.instructor.en].some((t) => t.toLowerCase().includes(term)))
      : rows;
  });

  protected export(): Promise<void> {
    return downloadFile(this.http, `${URL_BASE}/export`, 'training-overview.csv');
  }
}

import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalizedText } from '../../core/models';
import { AreaChart, ChartPoint, RankedBars, SegmentBar, Segment } from '../../shared/charts';
import { StatePanel } from '../../shared/state-panel';

interface MonthPoint {
  year: number;
  month: number;
  count: number;
}

interface Dashboard {
  enrolled: number;
  completed: number;
  completionPercent: number;
  certificates: number;
  averageGrade: number | null;
  cohortsUpcoming: number;
  cohortsRunning: number;
  cohortsFinished: number;
  enrollmentsByMonth: MonthPoint[];
  topCourses: { code: string; title: LocalizedText; enrolled: number; completionPercent: number }[];
  users: { total: number; byRole: { role: string; count: number }[]; newByMonth: MonthPoint[] } | null;
}

/** The numbers staff lead with, then the charts behind them. */
@Component({
  selector: 'app-insights',
  imports: [AreaChart, MatIconModule, RankedBars, RouterLink, SegmentBar, StatePanel, TranslatePipe],
  template: `
    @if (resource.error()) {
      <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
    } @else if (!data()) {
      <section class="panel"><app-state-panel kind="loading" /></section>
    } @else {
      @let view = data()!;

      <section class="tiles" [attr.aria-label]="'dashboard.summary' | translate">
        <div class="tile">
          <mat-icon class="blue" aria-hidden="true">groups</mat-icon>
          <strong>{{ view.enrolled }}</strong>
          <span>{{ 'insights.enrolled' | translate }}</span>
        </div>
        <div class="tile">
          <mat-icon class="amber" aria-hidden="true">event_available</mat-icon>
          <strong>{{ view.cohortsRunning }}</strong>
          <span>{{ 'insights.running' | translate }}</span>
        </div>
        <div class="tile">
          <mat-icon class="green" aria-hidden="true">task_alt</mat-icon>
          <strong>{{ view.completionPercent }}%</strong>
          <span>{{ 'insights.completion' | translate }}</span>
          <span class="meter" aria-hidden="true"><span [style.inline-size.%]="view.completionPercent"></span></span>
        </div>
        <div class="tile">
          <mat-icon class="plum" aria-hidden="true">workspace_premium</mat-icon>
          <strong>{{ view.certificates }}</strong>
          <span>{{ 'insights.certificates' | translate }}</span>
        </div>
        <div class="tile">
          <mat-icon class="blue" aria-hidden="true">grade</mat-icon>
          <strong>{{ view.averageGrade ?? '–' }}</strong>
          <span>{{ 'insights.average' | translate }}</span>
        </div>
        @if (view.users; as users) {
          <div class="tile">
            <mat-icon class="green" aria-hidden="true">person</mat-icon>
            <strong>{{ users.total }}</strong>
            <span>{{ 'insights.users' | translate }}</span>
          </div>
        }
      </section>

      <div class="charts">
        <section class="panel chart wide">
          <header>
            <h2>{{ 'insights.enrollmentsTitle' | translate }}</h2>
            <p>{{ 'insights.lastYear' | translate }}</p>
          </header>
          <app-area-chart [points]="months(view.enrollmentsByMonth)" [label]="'insights.enrollmentsTitle' | translate" />
        </section>

        <section class="panel chart">
          <header>
            <h2>{{ 'insights.cohortsTitle' | translate }}</h2>
            <p>{{ 'insights.cohortsTotal' | translate: { count: view.cohortsUpcoming + view.cohortsRunning + view.cohortsFinished } }}</p>
          </header>
          <app-segment-bar [parts]="cohorts()" />
        </section>

        <section class="panel chart">
          <header>
            <h2>{{ 'insights.topTitle' | translate }}</h2>
            <p>{{ 'insights.topSubtitle' | translate }}</p>
          </header>
          @if (top().length) {
            <app-ranked-bars [points]="top()" />
          } @else {
            <app-state-panel kind="empty" />
          }
        </section>

        <section class="panel chart">
          <header>
            <h2>{{ 'insights.completionTitle' | translate }}</h2>
            <p>{{ 'insights.completionSubtitle' | translate }}</p>
          </header>
          @if (completion().length) {
            <app-ranked-bars [points]="completion()" unit="%" [scale]="100" />
          } @else {
            <app-state-panel kind="empty" />
          }
        </section>

        @if (view.users; as users) {
          <section class="panel chart">
            <header>
              <h2>{{ 'insights.rolesTitle' | translate }}</h2>
              <a routerLink="/users">{{ 'nav.users' | translate }}</a>
            </header>
            <app-ranked-bars [points]="roles()" />
          </section>

          <section class="panel chart wide">
            <header>
              <h2>{{ 'insights.newUsersTitle' | translate }}</h2>
              <p>{{ 'insights.lastYear' | translate }}</p>
            </header>
            <app-area-chart [points]="months(users.newByMonth)" [label]="'insights.newUsersTitle' | translate" />
          </section>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
    }
    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(8.5rem, 1fr));
      gap: var(--space-3);
    }
    .tile {
      display: grid;
      align-content: start;
      gap: 2px;
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--line);
      border-radius: var(--radius-m);
      background: var(--mat-sys-surface);
      box-shadow: var(--shadow-card);
      transition:
        transform 0.18s ease,
        box-shadow 0.18s ease;
    }
    .tile:hover {
      transform: translateY(-2px);
      box-shadow: 0 14px 26px -18px rgb(6 56 74 / 35%);
    }
    .tile mat-icon {
      box-sizing: content-box;
      margin-block-end: var(--space-1);
      padding: 0.375rem;
      border-radius: 10px;
    }
    .blue {
      background: var(--tint-blue);
      color: var(--on-tint-blue);
    }
    .amber {
      background: var(--tint-amber);
      color: var(--on-tint-amber);
    }
    .green {
      background: var(--tint-green);
      color: var(--on-tint-green);
    }
    .plum {
      background: var(--tint-plum);
      color: var(--on-tint-plum);
    }
    .tile strong {
      font-size: 1.625rem;
      font-weight: 700;
      line-height: 1.2;
    }
    .tile > span {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .tile .meter {
      display: block;
      height: 0.25rem;
      margin-block-start: var(--space-1);
      overflow: hidden;
      border-radius: 2px;
      background: var(--mat-sys-surface-container-high);
    }
    .meter span {
      display: block;
      height: 100%;
      background: var(--brand-primary);
    }
    .charts {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 22rem), 1fr));
      gap: var(--space-4);
    }
    .chart {
      display: grid;
      align-content: start;
      gap: var(--space-3);
      padding: var(--space-4);
    }
    .wide {
      grid-column: 1 / -1;
    }
    .chart header {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--space-2);
    }
    h2 {
      font: var(--mat-sys-title-medium);
      font-weight: 600;
    }
    header p {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class Insights {
  private readonly languages = inject(LanguageService);
  private readonly translate = inject(TranslateService);

  protected readonly resource = httpResource<Dashboard>(() => '/api/v1/reports/dashboard');
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  private readonly arabic = computed(() => this.languages.language() === 'ar');

  protected readonly top = computed<ChartPoint[]>(() => (this.data()?.topCourses ?? []).map((c) => ({ label: this.courseName(c), value: c.enrolled })));
  protected readonly completion = computed<ChartPoint[]>(() =>
    [...(this.data()?.topCourses ?? [])].sort((a, b) => b.completionPercent - a.completionPercent).map((c) => ({ label: this.courseName(c), value: c.completionPercent })),
  );
  protected readonly roles = computed<ChartPoint[]>(() => (this.data()?.users?.byRole ?? []).map((r) => ({ label: this.translate.instant(`role.${r.role}`), value: r.count })));
  protected readonly cohorts = computed<Segment[]>(() => {
    const view = this.data();
    return [
      { label: this.translate.instant('insights.upcoming'), value: view?.cohortsUpcoming ?? 0, tone: 'one' },
      { label: this.translate.instant('insights.runningShort'), value: view?.cohortsRunning ?? 0, tone: 'two' },
      { label: this.translate.instant('insights.finished'), value: view?.cohortsFinished ?? 0, tone: 'three' },
    ];
  });

  protected months(points: MonthPoint[]): ChartPoint[] {
    const format = new Intl.DateTimeFormat(this.arabic() ? 'ar-SA-u-ca-gregory-nu-latn' : 'en-GB', { month: 'short' });
    return points.map((p) => ({ label: format.format(new Date(p.year, p.month - 1, 1)), value: p.count }));
  }

  private courseName(course: { code: string; title: LocalizedText }): string {
    return `${course.code} · ${this.arabic() ? course.title.ar : course.title.en}`;
  }
}

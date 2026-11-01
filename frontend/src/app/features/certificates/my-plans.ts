import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CourseRef } from './competencies-page';

interface MyCompetency {
  id: string;
  code: string;
  name: LocalizedText;
  description: LocalizedText;
  source: 'None' | 'Course' | 'SignOff';
  courses: CourseRef[];
}

interface MyPlan {
  id: string;
  name: LocalizedText;
  description: LocalizedText;
  dueOn: string | null;
  met: number;
  competencies: MyCompetency[];
}

@Component({
  selector: 'app-my-plans',
  imports: [AppDatePipe, LocalizedPipe, MatIconModule, MatProgressBarModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'plans.mine' | translate }}</h1>
          <p>{{ 'plans.mineSubtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        @for (plan of items(); track plan.id) {
          <section class="panel plan">
            <header>
              <h2>{{ plan.name | localized }}</h2>
              <p>{{ plan.description | localized }}</p>
              <small>
                {{ 'plans.progress' | translate: { met: plan.met, total: plan.competencies.length } }}
                @if (plan.dueOn) {
                  · {{ 'plans.due' | translate: { date: plan.dueOn | appDate } }}
                }
              </small>
              <mat-progress-bar mode="determinate" [value]="plan.competencies.length ? (plan.met / plan.competencies.length) * 100 : 0" />
            </header>
            <ul>
              @for (item of plan.competencies; track item.id) {
                <li>
                  <mat-icon [class.met]="item.source !== 'None'" aria-hidden="true">{{ icon(item) }}</mat-icon>
                  <div>
                    <h3>{{ item.name | localized }}</h3>
                    <p>{{ item.description | localized }}</p>
                    <small>{{ label(item) | translate }}</small>
                    @if (item.source === 'None' && item.courses.length) {
                      <small>
                        {{ 'plans.takeCourse' | translate }}
                        @for (course of item.courses; track course.id) {
                          <a routerLink="/cohorts"><span class="ltr-value">{{ course.code }}</span> {{ course.title | localized }}</a>
                        }
                      </small>
                    }
                  </div>
                </li>
              }
            </ul>
          </section>
        }
      }
    </div>
  `,
  styles: `
    .plan header {
      display: grid;
      gap: var(--space-1);
      padding: var(--space-4);
    }
    h2,
    h3,
    p {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-medium);
    }
    h3 {
      font: var(--mat-sys-title-small);
    }
    p,
    small {
      color: var(--mat-sys-on-surface-variant);
    }
    small {
      display: block;
    }
    small a {
      margin-inline-start: var(--space-2);
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
    mat-icon {
      flex: none;
      color: var(--mat-sys-outline);
    }
    mat-icon.met {
      color: var(--brand-primary);
    }
  `,
})
export class MyPlans {
  protected readonly resource = httpResource<MyPlan[]>(() => '/api/v1/learning-plans/my');
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected icon(item: MyCompetency): string {
    return item.source === 'Course' ? 'verified' : item.source === 'SignOff' ? 'how_to_reg' : 'radio_button_unchecked';
  }

  protected label(item: MyCompetency): string {
    return item.source === 'Course' ? 'plans.viaCourse' : item.source === 'SignOff' ? 'plans.viaSignOff' : 'plans.notMet';
  }
}

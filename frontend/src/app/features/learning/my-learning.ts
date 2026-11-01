import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { MyLearningItem } from './learning.models';
import { LEARNING_URL } from './learning.service';

@Component({
  selector: 'app-my-learning',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, MatProgressBarModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'nav.learn' | translate }}</h1>
          <p>{{ 'learn.subtitle' | translate }}</p>
        </div>
      </header>

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (items() === null) {
          <app-state-panel kind="loading" />
        } @else if (items()!.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (item of items(); track item.cohortId) {
              <li>
                <div class="main">
                  <h2>{{ item.courseTitle | localized }}</h2>
                  <p>{{ item.cohortName | localized }} · {{ item.startsOn | appDate }} – {{ item.endsOn | appDate }}</p>
                  <mat-progress-bar mode="determinate" [value]="item.percent" [attr.aria-label]="'learn.progress' | translate" />
                </div>
                <div class="side">
                  <strong class="ltr-value">{{ item.percent }}%</strong>
                  @if (item.completedAt) {
                    <span class="done"><mat-icon aria-hidden="true">verified</mat-icon>{{ 'learn.completed' | translate }}</span>
                  }
                  <a matButton [routerLink]="['/forum', item.cohortId]">{{ 'forum.title' | translate }}</a>
                  @if (item.hasStarted) {
                    <a matButton="filled" [routerLink]="['/learn', item.cohortId]">
                      {{ (item.percent > 0 ? 'learn.resume' : 'learn.start') | translate }}
                    </a>
                  } @else {
                    <span class="soon">{{ 'learn.startsOn' | translate: { date: (item.startsOn | appDate) } }}</span>
                  }
                </div>
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
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3) var(--space-6);
      padding: var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    .main {
      display: grid;
      flex: 1 1 20rem;
      gap: var(--space-2);
      min-width: 0;
    }
    h2 {
      font: var(--mat-sys-title-medium);
      font-weight: 600;
    }
    p,
    .soon {
      color: var(--mat-sys-on-surface-variant);
    }
    .side {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
    }
    .done {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      color: var(--mat-sys-primary);
      font: var(--mat-sys-label-large);
    }
  `,
})
export class MyLearning {
  protected readonly resource = httpResource<MyLearningItem[]>(() => `${LEARNING_URL}/my`);

  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
}

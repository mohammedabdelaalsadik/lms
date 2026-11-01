import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { FORUMS_URL, ForumCohort } from './community.service';

@Component({
  selector: 'app-forum-list',
  imports: [AppDatePipe, LocalizedPipe, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'forum.title' | translate }}</h1>
          <p>{{ 'forum.subtitle' | translate }}</p>
        </div>
      </header>

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (!items()) {
          <app-state-panel kind="loading" />
        } @else if (items()!.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (item of items(); track item.cohortId) {
              <li>
                <a [routerLink]="['/forum', item.cohortId]">
                  <mat-icon aria-hidden="true">forum</mat-icon>
                  <span>
                    <strong>{{ item.name | localized }}</strong>
                    <small>{{ item.startsOn | appDate }} – {{ item.endsOn | appDate }}</small>
                  </span>
                  <mat-icon class="flip-rtl" aria-hidden="true">chevron_right</mat-icon>
                </a>
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
      border-block-end: 1px solid var(--line);
    }
    a {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      color: inherit;
      text-decoration: none;

      &:hover {
        background: var(--mat-sys-surface-container-low);
      }
    }
    span {
      display: grid;
      flex: 1;
      gap: 2px;
      min-width: 0;
    }
    strong {
      font-weight: 500;
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class ForumList {
  protected readonly resource = httpResource<ForumCohort[]>(() => FORUMS_URL);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
}

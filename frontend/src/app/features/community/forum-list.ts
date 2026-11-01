import { httpResource } from '@angular/common/http';
import { Component, computed, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ChoiceChips } from '../../shared/choice-chips';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { FORUMS_URL, ForumCohort } from './community.service';

@Component({
  selector: 'app-forum-list',
  imports: [AppDatePipe, ChoiceChips, FilterBar, LocalizedPipe, MatIconModule, MatPaginatorModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'forum.title' | translate }}</h1>
          <p>{{ 'forum.subtitle' | translate }}</p>
        </div>
      </header>

      <app-filter-bar
        [label]="'forum.searchList' | translate"
        [search]="search()"
        [count]="items() ? shown().length : null"
        [active]="(search() ? 1 : 0) + (period() ? 1 : 0)"
        (searchChange)="find($event)"
        (clear)="clearFilters()"
      >
        <app-choice-chips [label]="'filters.period' | translate" [choices]="periodChoices" [value]="period()" (valueChange)="setPeriod($event)" />
      </app-filter-bar>

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (!items()) {
          <app-state-panel kind="loading" />
        } @else if (shown().length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (item of visible(); track item.cohortId) {
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

          <mat-paginator
            [length]="shown().length"
            [pageIndex]="page()"
            [pageSize]="pageSize()"
            [pageSizeOptions]="[10, 20, 50]"
            (page)="onPage($event)"
          />
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
  protected readonly search = signal('');
  protected readonly period = signal('');
  protected readonly page = signal(0);
  protected readonly pageSize = signal(10);
  protected readonly periodChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'upcoming', labelKey: 'filters.upcoming' },
    { value: 'running', labelKey: 'filters.running' },
    { value: 'ended', labelKey: 'filters.ended' },
  ];
  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();
    const today = new Date().toISOString().slice(0, 10);

    return (this.items() ?? []).filter((item) => {
      const inPeriod =
        !this.period() ||
        (this.period() === 'upcoming' && item.startsOn > today) ||
        (this.period() === 'running' && item.startsOn <= today && item.endsOn >= today) ||
        (this.period() === 'ended' && item.endsOn < today);

      return inPeriod && (!term || item.name.ar.toLowerCase().includes(term) || item.name.en.toLowerCase().includes(term));
    });
  });
  protected readonly visible = computed(() => {
    const start = this.page() * this.pageSize();
    return this.shown().slice(start, start + this.pageSize());
  });

  protected find(term: string): void {
    this.search.set(term);
    this.page.set(0);
  }

  protected setPeriod(value: string): void {
    this.period.set(value);
    this.page.set(0);
  }

  protected clearFilters(): void {
    this.period.set('');
    this.find('');
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }
}

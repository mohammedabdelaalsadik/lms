import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { urlFilters } from '../../core/filters/url-filters';
import { LocalizedText } from '../../core/models';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface Results {
  courses: { id: string; code: string; title: LocalizedText }[];
  cohorts: { id: string; name: LocalizedText }[];
  people: { id: string; name: LocalizedText; role: string }[];
}

const MIN_LENGTH = 2;

@Component({
  selector: 'app-search-page',
  imports: [FilterBar, LocalizedPipe, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'search.title' | translate }}</h1>
          <p>{{ 'search.subtitle' | translate }}</p>
        </div>
      </header>

      <app-filter-bar
        [label]="'search.box' | translate"
        [search]="filters.values.q()"
        [active]="filters.activeCount()"
        (searchChange)="filters.set('q', $event)"
        (clear)="filters.clear()"
      />

      @if (term().length >= min) {
        @if (resource.error()) {
          <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
        } @else if (!data()) {
          <section class="panel"><app-state-panel kind="loading" /></section>
        } @else if (nothing()) {
          <section class="panel"><app-state-panel kind="empty" /></section>
        } @else {
          @if (data()!.courses.length > 0) {
            <section class="panel">
              <h2>{{ 'nav.courses' | translate }}</h2>
              <ul>
                @for (course of data()!.courses; track course.id) {
                  <li><a routerLink="/courses" [queryParams]="{ search: course.code }"><mat-icon aria-hidden="true">menu_book</mat-icon>{{ course.title | localized }}</a></li>
                }
              </ul>
            </section>
          }
          @if (data()!.cohorts.length > 0) {
            <section class="panel">
              <h2>{{ 'nav.cohorts' | translate }}</h2>
              <ul>
                @for (cohort of data()!.cohorts; track cohort.id) {
                  <li><a [routerLink]="cohortLink(cohort.id)"><mat-icon aria-hidden="true">groups</mat-icon>{{ cohort.name | localized }}</a></li>
                }
              </ul>
            </section>
          }
          @if (data()!.people.length > 0) {
            <section class="panel">
              <h2>{{ 'nav.users' | translate }}</h2>
              <ul>
                @for (person of data()!.people; track person.id) {
                  <li><a routerLink="/users" [queryParams]="{ search: person.name.en }"><mat-icon aria-hidden="true">person</mat-icon>{{ person.name | localized }} · {{ 'role.' + person.role | translate }}</a></li>
                }
              </ul>
            </section>
          }
        }
      }
    </div>
  `,
  styles: `
    h2 {
      margin: 0;
      padding: var(--space-3) var(--space-4) var(--space-1);
      font: var(--mat-sys-title-small);
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    a {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-4);
      color: inherit;
      text-decoration: none;

      &:hover {
        background: var(--mat-sys-surface-container-low);
      }
    }
  `,
})
export class SearchPage {
  private readonly auth = inject(AuthService);

  protected readonly min = MIN_LENGTH;
  protected readonly filters = urlFilters({ q: '' });
  protected readonly term = computed(() => this.filters.values.q().trim());
  protected readonly resource = httpResource<Results>(() =>
    this.term().length >= MIN_LENGTH ? { url: '/api/v1/search', params: { q: this.term() } } : undefined,
  );
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly nothing = computed(() => {
    const found = this.data();
    return !!found && found.courses.length + found.cohorts.length + found.people.length === 0;
  });

  protected cohortLink(id: string): string[] {
    return this.auth.hasRole('Trainee') ? ['/learn', id] : ['/cohorts', id];
  }
}

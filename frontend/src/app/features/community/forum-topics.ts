import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { urlFilters } from '../../core/filters/url-filters';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CommunityService, FORUMS_URL, TopicList } from './community.service';

@Component({
  selector: 'app-forum-topics',
  imports: [
    AppDatePipe,
    FilterBar,
    FormsModule,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <div class="page">
      <a matButton routerLink="/forum" class="back">
        <mat-icon class="flip-rtl">arrow_back</mat-icon>
        {{ 'forum.title' | translate }}
      </a>

      <header class="page-header">
        <div>
          <h1>{{ data() ? (data()!.cohortName | localized) : ('forum.title' | translate) }}</h1>
          <p>{{ 'forum.topicsSubtitle' | translate }}</p>
        </div>
        @if (data() && !composing()) {
          <button matButton="filled" (click)="composing.set(true)">
            <mat-icon>add</mat-icon>
            {{ 'forum.newTopic' | translate }}
          </button>
        }
      </header>

      @if (composing()) {
        <form class="panel composer" (ngSubmit)="start()">
          <mat-form-field>
            <mat-label>{{ 'forum.topicTitle' | translate }}</mat-label>
            <input matInput name="title" maxlength="200" required [(ngModel)]="title" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'forum.topicBody' | translate }}</mat-label>
            <textarea matInput name="body" rows="5" maxlength="8000" required [(ngModel)]="body"></textarea>
          </mat-form-field>
          @if (data()?.groups?.length) {
            <mat-form-field>
              <mat-label>{{ 'groups.audience' | translate }}</mat-label>
              <mat-select name="audience" [ngModel]="audience()" (ngModelChange)="audience.set($event)">
                <mat-option value="">{{ 'groups.wholeCohort' | translate }}</mat-option>
                @for (group of data()!.groups; track group.id) {
                  <mat-option [value]="group.id">{{ group.name }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
          <div class="actions">
            <button type="button" matButton (click)="composing.set(false)">{{ 'common.cancel' | translate }}</button>
            <button type="submit" matButton="filled" [disabled]="busy() || !title().trim() || !body().trim()">{{ 'forum.post' | translate }}</button>
          </div>
        </form>
      }

      <app-filter-bar
        [label]="'forum.search' | translate"
        [search]="filters.values.search()"
        [count]="data()?.topics?.total ?? null"
        [active]="filters.activeCount()"
        (searchChange)="filters.set('search', $event)"
        (clear)="filters.clear()"
      />

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (!data()) {
          <app-state-panel kind="loading" />
        } @else if (data()!.topics.items.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (topic of data()!.topics.items; track topic.id) {
              <li>
                <a [routerLink]="['/forum', cohortId(), topic.id]">
                  <span class="text">
                    <strong>
                      @if (topic.isPinned) {
                        <mat-icon class="mark" [attr.aria-label]="'forum.pinned' | translate">push_pin</mat-icon>
                      }
                      @if (topic.isLocked) {
                        <mat-icon class="mark" [attr.aria-label]="'forum.locked' | translate">lock</mat-icon>
                      }
                      {{ topic.title }}
                      @if (groupName(topic.groupId); as name) {
                        <span class="group-tag">{{ name }}</span>
                      }
                    </strong>
                    <small>{{ topic.authorName | localized }} · {{ topic.lastActivityAt | appDate }}</small>
                  </span>
                  <span class="replies">{{ 'forum.replies' | translate: { count: topic.replyCount } }}</span>
                </a>
              </li>
            }
          </ul>
          <mat-paginator [length]="data()!.topics.total" [pageIndex]="filters.page()" [pageSize]="20" [hidePageSize]="true" (page)="onPage($event)" />
        }
      </section>
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .composer {
      display: grid;
      gap: var(--space-2);
      padding: var(--space-4);
    }
    .actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--space-2);
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      border-block-end: 1px solid var(--line);
    }
    a:not(.back) {
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
    .text {
      display: grid;
      flex: 1;
      gap: 2px;
      min-width: 0;
    }
    strong {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    small,
    .replies {
      color: var(--mat-sys-on-surface-variant);
    }
    .replies {
      flex: none;
    }
    .group-tag {
      margin-inline-start: var(--space-2);
      color: var(--brand-link);
      font-size: 0.8125rem;
    }
    .mark {
      width: 1.125rem;
      height: 1.125rem;
      color: var(--brand-link);
      font-size: 1.125rem;
      vertical-align: text-bottom;
    }
  `,
})
export class ForumTopics {
  private readonly community = inject(CommunityService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly cohortId = input.required<string>();

  protected readonly filters = urlFilters({ search: '' });
  protected readonly composing = signal(false);
  protected readonly busy = signal(false);
  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly audience = signal('');

  protected readonly resource = httpResource<TopicList>(() => ({
    url: `${FORUMS_URL}/${this.cohortId()}/topics`,
    params: { page: this.filters.page() + 1, pageSize: 20, search: this.filters.values.search() },
  }));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected groupName(id: string | null): string | null {
    return this.data()?.groups.find((g) => g.id === id)?.name ?? null;
  }

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
  }

  protected async start(): Promise<void> {
    this.busy.set(true);

    try {
      const id = await this.community.startTopic(this.cohortId(), this.title().trim(), this.body().trim(), this.audience() || null);
      await this.router.navigate(['/forum', this.cohortId(), id]);
    } catch {
      this.snackBar.open(this.translate.instant('community.sendFailed'), undefined, { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }
}

import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { urlFilters } from '../../core/filters/url-filters';
import { Paged } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ChoiceChips } from '../../shared/choice-chips';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { AppNotification, NOTIFICATIONS_URL, NotificationService, notificationIcon } from './notification.service';

@Component({
  selector: 'app-notifications-page',
  imports: [AppDatePipe, ChoiceChips, LocalizedPipe, MatButtonModule, MatIconModule, MatPaginatorModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'notifications.title' | translate }}</h1>
          <p>{{ 'notifications.subtitle' | translate }}</p>
        </div>
        @if (notifications.unread() > 0) {
          <button matButton="outlined" (click)="readAll()">{{ 'notifications.markAll' | translate }}</button>
        }
      </header>

      <app-choice-chips
        [label]="'notifications.title' | translate"
        [choices]="choices"
        [value]="filters.values.show()"
        (valueChange)="filters.set('show', $event)"
      />

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (!data()) {
          <app-state-panel kind="loading" />
        } @else if (data()!.items.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (item of data()!.items; track item.id) {
              <li>
                <button type="button" class="entry" [class.unread]="!item.isRead" (click)="open(item)">
                  <span class="icon"><mat-icon aria-hidden="true">{{ icon(item.kind) }}</mat-icon></span>
                  <span class="text">
                    <strong>{{ item.title | localized }}</strong>
                    <span>{{ item.body | localized }}</span>
                    <small>{{ item.createdAt | appDate }}</small>
                  </span>
                  @if (!item.isRead) {
                    <span class="dot"><span class="visually-hidden">{{ 'notifications.unread' | translate }}</span></span>
                  }
                </button>
              </li>
            }
          </ul>
          <mat-paginator [length]="data()!.total" [pageIndex]="filters.page()" [pageSize]="20" [hidePageSize]="true" (page)="onPage($event)" />
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
    .entry {
      display: flex;
      align-items: flex-start;
      gap: var(--space-3);
      width: 100%;
      padding: var(--space-3) var(--space-4);
      border: 0;
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;

      &:hover {
        background: var(--mat-sys-surface-container-low);
      }

      &:focus-visible {
        outline-offset: -2px;
      }
    }
    .icon {
      display: grid;
      flex: none;
      place-items: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: 12px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .text {
      display: grid;
      flex: 1;
      gap: 2px;
      min-width: 0;

      strong {
        font-weight: 500;
      }

      span,
      small {
        color: var(--mat-sys-on-surface-variant);
      }
    }
    .unread strong {
      font-weight: 700;
    }
    .dot {
      flex: none;
      width: 10px;
      height: 10px;
      margin-block-start: var(--space-2);
      border-radius: 50%;
      background: var(--brand-primary);
    }
  `,
})
export class NotificationsPage {
  private readonly router = inject(Router);

  protected readonly notifications = inject(NotificationService);
  protected readonly icon = notificationIcon;
  protected readonly filters = urlFilters({ show: '' });
  protected readonly choices = [
    { value: '', labelKey: 'common.all' },
    { value: 'unread', labelKey: 'notifications.unread' },
  ];

  protected readonly resource = httpResource<Paged<AppNotification>>(() => ({
    url: NOTIFICATIONS_URL,
    params: { page: this.filters.page() + 1, pageSize: 20, onlyUnread: this.filters.values.show() === 'unread' },
  }));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
  }

  protected async open(item: AppNotification): Promise<void> {
    if (!item.isRead) {
      await this.notifications.markRead(item.id);
    }

    await this.router.navigateByUrl(item.link);
  }

  protected async readAll(): Promise<void> {
    await this.notifications.markAllRead();
    this.resource.reload();
  }
}

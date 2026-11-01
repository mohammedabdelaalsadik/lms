import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { AppNotification, NotificationService, notificationIcon } from './notification.service';

const REFRESH_MS = 60_000;

@Component({
  selector: 'app-notification-bell',
  imports: [LocalizedPipe, MatBadgeModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, RouterLink, TranslatePipe],
  template: `
    <button
      matIconButton
      [matMenuTriggerFor]="panel"
      [matTooltip]="'notifications.title' | translate"
      [attr.aria-label]="label() | translate: { count: notifications.unread() }"
      (menuOpened)="load()"
    >
      <mat-icon
        [matBadge]="notifications.unread()"
        [matBadgeHidden]="notifications.unread() === 0"
        matBadgeSize="medium"
        matBadgeColor="warn"
        aria-hidden="false"
      >notifications</mat-icon>
    </button>

    <mat-menu #panel="matMenu" class="notification-menu" xPosition="before">
      <div class="head" (click)="$event.stopPropagation()">
        <strong>{{ 'notifications.title' | translate }}</strong>
        @if (notifications.unread() > 0) {
          <button matButton (click)="readAll()">{{ 'notifications.markAll' | translate }}</button>
        }
      </div>

      @if (items() === null) {
        <p class="note">{{ 'common.loading' | translate }}</p>
      } @else if (items()!.length === 0) {
        <p class="note">{{ 'notifications.empty' | translate }}</p>
      }

      @for (item of items(); track item.id) {
        <button mat-menu-item class="item" [class.unread]="!item.isRead" (click)="open(item)">
          <mat-icon>{{ icon(item.kind) }}</mat-icon>
          <span class="title">{{ item.title | localized }}</span>
        </button>
      }

      <a mat-menu-item class="all" routerLink="/notifications">{{ 'notifications.viewAll' | translate }}</a>
    </mat-menu>
  `,
  styles: `
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      min-height: 2.75rem;
      padding-inline: var(--space-4) var(--space-2);
    }
    .note {
      padding: var(--space-4);
      color: var(--mat-sys-on-surface-variant);
    }
    .item.unread .title {
      font-weight: 600;
    }
    .title {
      white-space: normal;
    }
    .all {
      border-block-start: 1px solid var(--line);
      color: var(--brand-link);
    }
  `,
})
export class NotificationBell implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private timer?: ReturnType<typeof setInterval>;

  protected readonly notifications = inject(NotificationService);
  protected readonly items = signal<AppNotification[] | null>(null);
  protected readonly label = computed(() => (this.notifications.unread() > 0 ? 'notifications.unreadLabel' : 'notifications.title'));
  protected readonly icon = notificationIcon;

  ngOnInit(): void {
    void this.notifications.refreshCount();
    this.timer = setInterval(() => {
      if (!document.hidden) {
        void this.notifications.refreshCount();
      }
    }, REFRESH_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected async load(): Promise<void> {
    this.items.set(null);
    this.items.set((await this.notifications.latest()).items);
    void this.notifications.refreshCount();
  }

  protected async open(item: AppNotification): Promise<void> {
    if (!item.isRead) {
      await this.notifications.markRead(item.id);
    }

    await this.router.navigateByUrl(item.link);
  }

  protected async readAll(): Promise<void> {
    await this.notifications.markAllRead();
    await this.load();
  }
}

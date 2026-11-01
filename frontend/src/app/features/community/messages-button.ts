import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { CommunityService } from './community.service';

const REFRESH_MS = 60_000;

@Component({
  selector: 'app-messages-button',
  imports: [MatBadgeModule, MatButtonModule, MatIconModule, MatTooltipModule, RouterLink, TranslatePipe],
  template: `
    <a
      matIconButton
      routerLink="/messages"
      [matTooltip]="'messages.title' | translate"
      [attr.aria-label]="label() | translate: { count: community.unreadMessages() }"
    >
      <mat-icon
        [matBadge]="community.unreadMessages()"
        [matBadgeHidden]="community.unreadMessages() === 0"
        matBadgeSize="medium"
        matBadgeColor="warn"
        aria-hidden="false"
      >chat</mat-icon>
    </a>
  `,
})
export class MessagesButton implements OnInit, OnDestroy {
  private timer?: ReturnType<typeof setInterval>;

  protected readonly community = inject(CommunityService);
  protected readonly label = computed(() => (this.community.unreadMessages() > 0 ? 'messages.unread' : 'messages.title'));

  ngOnInit(): void {
    void this.community.refreshUnread();
    this.timer = setInterval(() => {
      if (!document.hidden) {
        void this.community.refreshUnread();
      }
    }, REFRESH_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}

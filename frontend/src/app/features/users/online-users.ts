import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { LocalizedText } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface OnlineUser {
  name: LocalizedText;
  role: string;
  lastSeenAt: string;
}

@Component({
  selector: 'app-online-users',
  imports: [LocalizedPipe, MatButtonModule, MatIconModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'online.title' | translate }}</h1>
          <p>{{ 'online.subtitle' | translate }}</p>
        </div>
        <button matButton="outlined" (click)="resource.reload()"><mat-icon>refresh</mat-icon>{{ 'online.refresh' | translate }}</button>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <section class="panel">
          <p class="count">{{ 'online.count' | translate: { count: items()!.length } }}</p>
          @for (person of items(); track $index) {
            <article>
              <span class="dot" aria-hidden="true"></span>
              <span class="name">{{ person.name | localized }}</span>
              <small>{{ 'role.' + person.role | translate }}</small>
            </article>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .count {
      margin: 0;
      padding: var(--space-3) var(--space-4);
      font: var(--mat-sys-title-small);
    }
    article {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border-block-start: 1px solid var(--line);
    }
    .dot {
      width: 0.625rem;
      height: 0.625rem;
      border-radius: 50%;
      background: light-dark(#1a9c6e, #4fd1a5);
    }
    .name {
      flex: 1;
      min-width: 0;
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class OnlineUsers {
  protected readonly resource = httpResource<OnlineUser[]>(() => '/api/v1/users/online');
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
}

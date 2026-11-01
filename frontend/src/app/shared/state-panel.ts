import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Shared loading, empty and error presentation for data screens. */
@Component({
  selector: 'app-state-panel',
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @switch (kind()) {
      @case ('loading') {
        <div class="skeletons" role="status" [attr.aria-label]="'common.loading' | translate">
          @for (row of [1, 2, 3, 4]; track row) {
            <div class="skeleton"></div>
          }
        </div>
      }
      @case ('error') {
        <div class="message" role="alert">
          <mat-icon aria-hidden="true">cloud_off</mat-icon>
          <h2>{{ 'common.loadFailed' | translate }}</h2>
          <button matButton="outlined" (click)="retry.emit()">{{ 'common.retry' | translate }}</button>
        </div>
      }
      @case ('empty') {
        <div class="message" role="status">
          <mat-icon aria-hidden="true">search_off</mat-icon>
          <h2>{{ 'common.empty' | translate }}</h2>
        </div>
      }
    }
  `,
  styles: `
    .skeletons {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
    }
    .skeleton {
      height: 2.5rem;
      border-radius: var(--radius-s);
      background: linear-gradient(
        100deg,
        var(--mat-sys-surface-container) 30%,
        var(--mat-sys-surface-container-high) 50%,
        var(--mat-sys-surface-container) 70%
      );
      background-size: 220% 100%;
      animation: shimmer 1.4s linear infinite;
    }
    .message {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-8) var(--space-4);
      color: var(--mat-sys-on-surface-variant);
    }
    h2 {
      font: var(--mat-sys-title-small);
    }
    @keyframes shimmer {
      from {
        background-position: 120% 0;
      }
      to {
        background-position: -120% 0;
      }
    }
  `,
})
export class StatePanel {
  readonly kind = input.required<'loading' | 'error' | 'empty'>();
  readonly retry = output<void>();
}

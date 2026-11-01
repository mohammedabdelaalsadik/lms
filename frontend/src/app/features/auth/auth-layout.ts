import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { BrandMark } from '../../shared/brand-mark';
import { DriftLetters, SoundBars } from '../../shared/brand-motion';
import { ToolbarControls } from '../../shared/toolbar-controls';

@Component({
  selector: 'app-auth-layout',
  imports: [BrandMark, DriftLetters, SoundBars, ToolbarControls, TranslatePipe],
  template: `
    <div class="layout">
      <aside>
        <app-drift-letters />
        <div class="brand"><app-brand-mark /> {{ 'app.name' | translate }}</div>
        <div class="pitch">
          <h2>{{ 'auth.pitchTitle' | translate }}</h2>
          <ol>
            <li>{{ 'auth.pitch1' | translate }}</li>
            <li>{{ 'auth.pitch2' | translate }}</li>
            <li>{{ 'auth.pitch3' | translate }}</li>
          </ol>
          <app-sound-bars />
        </div>
      </aside>
      <main>
        <div class="controls"><app-toolbar-controls /></div>
        <div class="form-area">
          <div class="card"><ng-content /></div>
        </div>
      </main>
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      min-height: 100dvh;
      grid-template-columns: 1fr;
      background: var(--page-wash), var(--page);
    }
    aside {
      position: relative;
      isolation: isolate;
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      overflow: hidden;
      min-height: 9rem;
      padding: var(--space-6);
      background: var(--brand-scene);
      color: #ffffff;
      --brand-primary: #4fc3dc;
    }
    .brand,
    .pitch {
      position: relative;
      z-index: 1;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      font: var(--mat-sys-title-medium);
      font-weight: 600;

      app-brand-mark {
        color: inherit;
      }
    }
    .pitch {
      display: none;
      max-width: 28rem;
      animation: rise 0.7s 0.1s cubic-bezier(0.22, 1, 0.36, 1) both;

      h2 {
        font-size: 2.25rem;
        font-weight: 700;
        line-height: 1.3;
        letter-spacing: -0.01em;
      }
    }
    app-sound-bars {
      margin-block-start: var(--space-6);
    }
    ol {
      display: grid;
      gap: var(--space-3);
      margin: var(--space-6) 0 0;
      padding: 0;
      list-style: none;
      counter-reset: step;
    }
    li {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      counter-increment: step;
      font: var(--mat-sys-body-large);

      &::before {
        content: counter(step);
        display: grid;
        flex: none;
        place-items: center;
        width: 1.75rem;
        height: 1.75rem;
        border-radius: 50%;
        background: linear-gradient(#ffd27a, #ff7a59);
        color: #2b1d04;
        font: var(--mat-sys-label-large);
        font-weight: 700;
      }
    }
    main {
      display: flex;
      flex-direction: column;
      padding: var(--space-4);
    }
    .controls {
      display: flex;
      justify-content: flex-end;
    }
    .form-area {
      display: flex;
      flex: 1;
      align-items: center;
      justify-content: center;
      padding-block: var(--space-4);
    }
    .card {
      display: flex;
      justify-content: center;
      width: min(100%, 28rem);
      padding: var(--space-6);
      background: var(--mat-sys-surface);
      border: 1px solid var(--line);
      border-radius: 18px;
      box-shadow: var(--shadow-card);
      animation: rise 0.52s cubic-bezier(0.22, 1, 0.36, 1) both;

      &:has(.wide) {
        width: min(100%, 36rem);
      }
    }
    @media (min-width: 600px) {
      .card {
        padding: var(--space-8);
      }
    }
    @media (min-width: 960px) {
      .layout {
        grid-template-columns: minmax(24rem, 5fr) 6fr;
      }
      aside {
        min-height: 100dvh;
        padding: var(--space-8) 3rem;
      }
      .pitch {
        display: block;
      }
      app-drift-letters {
        inset-block: 6rem 45%;
      }
    }
    @keyframes rise {
      from {
        opacity: 0;
        transform: translateY(10px);
      }
    }
  `,
})
export class AuthLayout {}

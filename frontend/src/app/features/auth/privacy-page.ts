import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { StatePanel } from '../../shared/state-panel';

export type PrivacyKind = 'Export' | 'Erasure';

export interface PrivacyRequest {
  id: string;
  kind: PrivacyKind;
  status: 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
  reason: string | null;
  requestedAt: string;
  decidedAt: string | null;
  decisionNote: string | null;
  downloadUntil: string | null;
}

export const PRIVACY_URL = '/api/v1/privacy';

@Component({
  selector: 'app-privacy-page',
  imports: [AppDatePipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'privacy.title' | translate }}</h1>
          <p>{{ 'privacy.subtitle' | translate }}</p>
        </div>
      </header>

      <div class="choices">
        <section class="panel choice">
          <mat-icon aria-hidden="true">download</mat-icon>
          <h2>{{ 'privacy.exportTitle' | translate }}</h2>
          <p>{{ 'privacy.exportText' | translate }}</p>
          <button matButton="outlined" [disabled]="busy()" (click)="ask('Export', null)">{{ 'privacy.exportAsk' | translate }}</button>
        </section>

        @if (canErase()) {
          <section class="panel choice">
            <mat-icon aria-hidden="true">person_off</mat-icon>
            <h2>{{ 'privacy.erasureTitle' | translate }}</h2>
            <p>{{ 'privacy.erasureText' | translate }}</p>
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'privacy.reason' | translate }}</mat-label>
              <input #reason matInput maxlength="500" />
            </mat-form-field>
            <button matButton="outlined" [disabled]="busy()" (click)="askErasure(reason)">{{ 'privacy.erasureAsk' | translate }}</button>
          </section>
        }
      </div>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (items()?.length) {
        <section class="panel list">
          <h2>{{ 'privacy.history' | translate }}</h2>
          @for (item of items(); track item.id) {
            <article>
              <div>
                <h3>{{ 'privacy.kind' + item.kind | translate }}</h3>
                <small>{{ item.requestedAt | appDate }} · {{ 'privacy.status' + item.status | translate }}</small>
                @if (item.decisionNote) {
                  <p>{{ item.decisionNote }}</p>
                }
              </div>
              @if (item.status === 'Pending') {
                <button matButton [disabled]="busy()" (click)="cancel(item)">{{ 'common.cancel' | translate }}</button>
              } @else if (canDownload(item)) {
                <a matButton="filled" [href]="downloadUrl(item)" download>{{ 'privacy.download' | translate }}</a>
              }
            </article>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .choices {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr));
      gap: var(--space-4);
      align-items: start;
    }
    .choice {
      display: grid;
      justify-items: start;
      gap: var(--space-2);
      padding: var(--space-4);

      mat-form-field {
        width: 100%;
      }
    }
    .choice mat-icon {
      color: var(--brand-primary);
    }
    h2,
    h3,
    p {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-medium);
    }
    h3 {
      font: var(--mat-sys-title-small);
    }
    p,
    small {
      color: var(--mat-sys-on-surface-variant);
    }
    .list > h2 {
      padding: var(--space-4) var(--space-4) var(--space-2);
    }
    article {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
  `,
})
export class PrivacyPage {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<PrivacyRequest[]>(() => `${PRIVACY_URL}/my`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly canErase = computed(() => this.auth.hasRole('Trainee'));

  protected canDownload(item: PrivacyRequest): boolean {
    return item.downloadUntil !== null && new Date(item.downloadUntil) > new Date();
  }

  protected downloadUrl(item: PrivacyRequest): string {
    return `${PRIVACY_URL}/my/${item.id}/download`;
  }

  protected ask(kind: PrivacyKind, reason: string | null): Promise<void> {
    return this.run(this.http.post(`${PRIVACY_URL}/my`, { kind, reason }));
  }

  protected async askErasure(box: HTMLInputElement): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '28rem',
      data: { titleKey: 'privacy.erasureConfirmTitle', messageKey: 'privacy.erasureConfirm', confirmKey: 'privacy.erasureAsk' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.ask('Erasure', box.value);
      box.value = '';
    }
  }

  protected cancel(item: PrivacyRequest): Promise<void> {
    return this.run(this.http.post(`${PRIVACY_URL}/my/${item.id}/cancel`, {}));
  }

  private async run(call: Observable<unknown>): Promise<void> {
    this.busy.set(true);

    try {
      await firstValueFrom(call);
      this.resource.reload();
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
    } finally {
      this.busy.set(false);
    }
  }
}

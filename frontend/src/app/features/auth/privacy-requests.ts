import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { PRIVACY_URL, PrivacyRequest } from './privacy-page';

interface QueueItem {
  request: PrivacyRequest;
  person: LocalizedText;
  role: string;
}

@Component({
  selector: 'app-privacy-requests',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatFormFieldModule, MatInputModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'privacy.queueTitle' | translate }}</h1>
          <p>{{ 'privacy.queueSubtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        <section class="panel">
          @for (item of items(); track item.request.id) {
            <article>
              <div class="who">
                <h2>{{ item.person | localized }}</h2>
                <small>{{ 'privacy.kind' + item.request.kind | translate }} · {{ item.request.requestedAt | appDate }} · {{ 'privacy.status' + item.request.status | translate }}</small>
                @if (item.request.reason) {
                  <p>{{ item.request.reason }}</p>
                }
                @if (item.request.decisionNote) {
                  <p>{{ item.request.decisionNote }}</p>
                }
              </div>
              @if (canDecide() && item.request.status === 'Pending') {
                <div class="decide">
                  <mat-form-field subscriptSizing="dynamic">
                    <mat-label>{{ 'privacy.refuseReason' | translate }}</mat-label>
                    <input #note matInput maxlength="500" />
                  </mat-form-field>
                  <button matButton [disabled]="busy()" (click)="reject(item, note)">{{ 'privacy.refuse' | translate }}</button>
                  <button matButton="filled" [disabled]="busy()" (click)="approve(item)">{{ 'privacy.approve' | translate }}</button>
                </div>
              }
            </article>
          }
        </section>
      }
    </div>
  `,
  styles: `
    article {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
    }
    article + article {
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
    .who {
      flex: 1 1 16rem;
      min-width: 0;
    }
    .decide {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
    h2,
    p {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-small);
    }
    p,
    small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class PrivacyRequests {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<QueueItem[]>(() => `${PRIVACY_URL}/requests`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly canDecide = computed(() => this.auth.hasRole('Admin'));

  protected async approve(item: QueueItem): Promise<void> {
    if (item.request.kind === 'Erasure') {
      const ref = this.dialog.open(ConfirmDialog, {
        width: '28rem',
        data: { titleKey: 'privacy.approveTitle', messageKey: 'privacy.approveMessage', confirmKey: 'privacy.approve' },
      });

      if (!(await firstValueFrom(ref.afterClosed()))) {
        return;
      }
    }

    await this.run(this.http.post(`${PRIVACY_URL}/requests/${item.request.id}/approve`, {}));
  }

  protected reject(item: QueueItem, box: HTMLInputElement): Promise<void> {
    return this.run(this.http.post(`${PRIVACY_URL}/requests/${item.request.id}/reject`, { note: box.value }));
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

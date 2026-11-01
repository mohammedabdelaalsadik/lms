import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { contentUrl } from './learning.service';

interface BinEntry {
  id: string;
  kind: 'Section' | 'Activity';
  title: LocalizedText;
  removedAt: string;
  expiresAt: string;
}

@Component({
  selector: 'app-recycle-bin',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/courses', id(), 'content']" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'content.manage' | translate }}</a>

      <header class="page-header">
        <div>
          <h1>{{ 'bin.title' | translate }}</h1>
          <p>{{ 'bin.subtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        <section class="panel list">
          @for (item of items(); track item.id) {
            <article>
              <mat-icon aria-hidden="true">{{ item.kind === 'Section' ? 'folder' : 'article' }}</mat-icon>
              <div>
                <h2>{{ item.title | localized }}</h2>
                <small>{{ (item.kind === 'Section' ? 'bin.section' : 'bin.activity') | translate }} · {{ 'bin.expires' | translate: { date: item.expiresAt | appDate } }}</small>
              </div>
              <button matButton="outlined" [disabled]="busy()" (click)="restore(item)">{{ 'bin.restore' | translate }}</button>
              @if (canPurge()) {
                <button matButton [disabled]="busy()" (click)="purge(item)">{{ 'bin.purge' | translate }}</button>
              }
            </article>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    article {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
    }
    article + article {
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
    article > div {
      flex: 1 1 14rem;
      min-width: 0;
    }
    mat-icon {
      color: var(--mat-sys-on-surface-variant);
    }
    h2 {
      margin: 0;
      font: var(--mat-sys-title-small);
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class RecycleBin {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<BinEntry[]>(() => this.url());
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly canPurge = computed(() => this.auth.hasRole('Admin', 'Coordinator'));

  private url(): string {
    return `${contentUrl(this.id())}/bin`;
  }

  protected restore(item: BinEntry): Promise<void> {
    return this.run(this.http.post(`${this.url()}/${item.id}/restore`, {}));
  }

  protected async purge(item: BinEntry): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'bin.purgeTitle', messageKey: 'bin.purgeMessage', confirmKey: 'bin.purge' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.http.delete(`${this.url()}/${item.id}`));
    }
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

import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { CohortRoster, RosterEntry } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { NationalIdDialog } from '../../shared/national-id-dialog';
import { StatePanel } from '../../shared/state-panel';
import { COHORTS_URL, CohortService } from './cohort.service';
import { RejectDialog } from './reject-dialog';

@Component({
  selector: 'app-cohort-detail',
  imports: [
    AppDatePipe,
    LocalizedPipe,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './cohort-detail.html',
  styles: `
    .back {
      align-self: flex-start;
    }
    .scroll {
      overflow-x: auto;
    }
    table {
      min-width: 44rem;
    }
    .facts {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--space-4);
      padding: var(--space-4);

      dt {
        color: var(--mat-sys-on-surface-variant);
      }
      dd {
        margin: var(--space-1) 0 0;
        font-weight: 600;
      }
    }
    .reason {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
    .row-actions {
      display: flex;
      gap: var(--space-2);
    }
    .header-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class CohortDetail {
  private readonly cohorts = inject(CohortService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly columns = ['name', 'nationalId', 'requested', 'status', 'actions'];

  protected readonly resource = httpResource<CohortRoster>(() => `${COHORTS_URL}/${this.id()}/roster`);

  protected readonly data = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

  protected reveal(entry: RosterEntry): void {
    const name = this.translate.getCurrentLang() === 'ar' ? entry.traineeName.ar : entry.traineeName.en;
    this.dialog.open(NationalIdDialog, { data: { userId: entry.traineeId, name }, width: '26rem', maxWidth: 'calc(100vw - 2rem)' });
  }

  protected approve(entry: RosterEntry): Promise<void> {
    return this.run(this.cohorts.approve(this.data()!.cohort, entry.enrollmentId), 'roster.approved');
  }

  protected async reject(entry: RosterEntry): Promise<void> {
    const ref = this.dialog.open(RejectDialog, { width: '28rem', maxWidth: 'calc(100vw - 2rem)' });
    const reason = await firstValueFrom(ref.afterClosed());

    if (reason) {
      await this.run(
        this.cohorts.reject(this.data()!.cohort, entry.enrollmentId, reason),
        'roster.rejected',
      );
    }
  }

  protected async close(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: {
        titleKey: 'cohorts.closeTitle',
        messageKey: 'cohorts.closeMessage',
        confirmKey: 'cohorts.close',
      },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.cohorts.close(this.id()), 'cohorts.closed');
    }
  }

  private async run(action: Observable<void>, successKey: string): Promise<void> {
    try {
      await firstValueFrom(action);
      this.notify(successKey);
    } catch (error) {
      this.notify(errorKey(error));
    }

    this.resource.reload();
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

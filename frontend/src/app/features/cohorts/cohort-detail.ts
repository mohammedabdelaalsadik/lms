import { FilterBar } from '../../shared/filter-bar';
import { ChoiceChips } from '../../shared/choice-chips';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { LIVE_CLASSES_ENABLED } from '../../core/features';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
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
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
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
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);

      a {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        min-height: 2.25rem;
        padding: 0 var(--space-3) 0 var(--space-2);
        border: 1px solid var(--line);
        border-radius: 999px;
        background: var(--mat-sys-surface);
        color: var(--brand-link);
        font: var(--mat-sys-label-large);
        text-decoration: none;
        transition:
          background-color 0.15s ease,
          border-color 0.15s ease;

        &:hover {
          border-color: var(--brand-primary);
          background: var(--mat-sys-primary-container);
        }
      }

      mat-icon {
        width: 18px;
        height: 18px;
        font-size: 18px;
      }
    }
    .roster {
      display: grid;
      gap: var(--space-3);

      h2 {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font: var(--mat-sys-title-large);
        font-weight: 700;
      }
    }
    .total {
      min-width: 1.75rem;
      padding: 0 var(--space-2);
      border-radius: 999px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
      font: var(--mat-sys-label-large);
      font-weight: 700;
      text-align: center;
    }
  `,
})
export class CohortDetail {
  protected readonly liveClasses = LIVE_CLASSES_ENABLED;
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

  protected readonly search = signal('');
  protected readonly status = signal('');
  protected readonly page = signal(0);
  protected readonly pageSize = signal(10);

  protected readonly statusChoices = computed(() => {
    const present = [...new Set((this.data()?.entries ?? []).map((entry) => entry.status))];
    return [{ value: '', labelKey: 'common.all' }, ...present.map((value) => ({ value, labelKey: `enrollmentStatus.${value}` }))];
  });

  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();

    return (this.data()?.entries ?? []).filter(
      (entry) =>
        (!this.status() || entry.status === this.status()) &&
        (!term || entry.traineeName.ar.toLowerCase().includes(term) || entry.traineeName.en.toLowerCase().includes(term)),
    );
  });

  protected readonly visible = computed(() => {
    const start = this.page() * this.pageSize();
    return this.shown().slice(start, start + this.pageSize());
  });

  protected find(term: string): void {
    this.search.set(term);
    this.page.set(0);
  }

  protected setStatus(value: string): void {
    this.status.set(value);
    this.page.set(0);
  }

  protected clearFilters(): void {
    this.status.set('');
    this.find('');
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

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

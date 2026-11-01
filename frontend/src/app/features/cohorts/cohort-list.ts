import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { urlFilters } from '../../core/filters/url-filters';
import { errorKey } from '../../core/http/api-error';
import { Cohort, Paged } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ChoiceChips } from '../../shared/choice-chips';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CohortFormDialog } from './cohort-form-dialog';
import { COHORTS_URL, CohortService } from './cohort.service';

@Component({
  selector: 'app-cohort-list',
  imports: [
    AppDatePipe,
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './cohort-list.html',
  styleUrl: './cohort-list.scss',
})
export class CohortList {
  private readonly cohorts = inject(CohortService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  protected readonly isTrainee = computed(() => this.auth.hasRole('Trainee'));
  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly canOpenRoster = computed(() =>
    this.auth.hasRole('Admin', 'Coordinator', 'Instructor'),
  );
  protected readonly statusChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'Open', labelKey: 'cohortStatus.Open' },
    { value: 'Closed', labelKey: 'cohortStatus.Closed' },
  ];
  protected readonly filters = urlFilters({ search: '', status: '' });
  protected readonly pageSize = signal(10);
  protected readonly working = signal<string | null>(null);

  protected readonly resource = httpResource<Paged<Cohort>>(() => ({
    url: COHORTS_URL,
    params: {
      page: this.filters.page() + 1,
      pageSize: this.pageSize(),
      ...(this.filters.values.search() ? { search: this.filters.values.search() } : {}),
      ...(this.filters.values.status() ? { status: this.filters.values.status() } : {}),
    },
  }));

  protected readonly data = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  protected async openForm(): Promise<void> {
    const ref = this.dialog.open(CohortFormDialog, {
      width: '40rem',
      maxWidth: 'calc(100vw - 2rem)',
    });

    if (await firstValueFrom(ref.afterClosed())) {
      this.notify('cohorts.saved');
      this.resource.reload();
    }
  }

  protected async enroll(cohort: Cohort): Promise<void> {
    this.working.set(cohort.id);

    try {
      const status = await firstValueFrom(this.cohorts.enroll(cohort.id));
      this.notify(status === 'Pending' ? 'cohorts.requestSent' : 'cohorts.enrolled');
    } catch (error) {
      this.notify(errorKey(error));
    } finally {
      this.working.set(null);
      this.resource.reload();
    }
  }

  protected async withdraw(cohort: Cohort): Promise<void> {
    this.working.set(cohort.id);

    try {
      await firstValueFrom(this.cohorts.withdraw(cohort.id));
      this.notify('cohorts.withdrawn');
    } catch (error) {
      this.notify(errorKey(error));
    } finally {
      this.working.set(null);
      this.resource.reload();
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

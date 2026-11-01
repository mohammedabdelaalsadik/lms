import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { urlFilters } from '../../core/filters/url-filters';
import { errorKey } from '../../core/http/api-error';
import { Paged, Role, UserSummary } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ChoiceChips } from '../../shared/choice-chips';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { NationalIdDialog } from '../../shared/national-id-dialog';
import { ReasonDialog } from '../../shared/reason-dialog';
import { StatePanel } from '../../shared/state-panel';

@Component({
  selector: 'app-user-list',
  imports: [
    AppDatePipe,
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatSelectModule,
    MatTableModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './user-list.html',
  styles: `
    .scroll {
      overflow-x: auto;
    }
    table {
      min-width: 44rem;
    }
    .role-select {
      width: 11rem;
      padding-block: var(--space-1);
    }
  `,
})
export class UserList {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  protected readonly auth = inject(AuthService);

  protected readonly roles: Role[] = ['Trainee', 'Instructor', 'Coordinator', 'Admin', 'Auditor'];
  protected readonly canAssign = computed(() => this.auth.hasRole('Admin'));
  protected readonly columns = ['name', 'nationalId', 'email', 'role', 'joined'];
  protected readonly roleChoices = [
    { value: '', labelKey: 'common.all' },
    ...this.roles.map((role) => ({ value: role, labelKey: `role.${role}` })),
  ];
  protected readonly filters = urlFilters({ search: '', role: '' });
  protected readonly pageSize = signal(20);

  protected readonly resource = httpResource<Paged<UserSummary>>(() => ({
    url: '/api/v1/users',
    params: {
      page: this.filters.page() + 1,
      pageSize: this.pageSize(),
      ...(this.filters.values.search() ? { search: this.filters.values.search() } : {}),
      ...(this.filters.values.role() ? { role: this.filters.values.role() } : {}),
    },
  }));

  protected readonly data = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

  protected reveal(user: UserSummary): void {
    const name = this.translate.getCurrentLang() === 'ar' ? user.fullName.ar : user.fullName.en;
    this.dialog.open(NationalIdDialog, { data: { userId: user.id, name }, width: '26rem', maxWidth: 'calc(100vw - 2rem)' });
  }

  protected async resetMfa(user: UserSummary): Promise<void> {
    const ref = this.dialog.open(ReasonDialog, {
      width: '26rem',
      data: { titleKey: 'security.reset', labelKey: 'security.resetLabel', messageKey: 'security.resetMessage', confirmKey: 'security.reset' },
    });
    const reason = await firstValueFrom(ref.afterClosed());

    if (!reason) {
      return;
    }

    try {
      await firstValueFrom(this.http.post<void>(`/api/v1/users/${user.id}/mfa/reset`, { reason }));
      this.notify('security.resetDone');
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  protected async changeRole(user: UserSummary, role: Role): Promise<void> {
    try {
      await firstValueFrom(this.http.put<void>(`/api/v1/users/${user.id}/role`, { role }));
      this.notify('users.roleChanged');
    } catch (error) {
      this.notify(errorKey(error));
    }

    this.resource.reload();
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }
}

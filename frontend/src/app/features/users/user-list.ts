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
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
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
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { StatePanel } from '../../shared/state-panel';
import { TemporaryPasswordDialog, UserEditDialog } from './user-dialogs';

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
    MatTooltipModule,
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
      min-width: 60rem;
    }
    td.mat-column-actions,
    th.mat-column-actions {
      width: 1%;
      white-space: nowrap;
    }
    .person {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      padding-block: var(--space-2);
    }
    .avatar {
      display: grid;
      flex: none;
      place-items: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: 50%;
      background: var(--tint-blue);
      color: var(--on-tint-blue);
      font-weight: 700;

      &.off {
        background: var(--tint-grey);
        color: var(--on-tint-grey);
      }
    }
    .who {
      display: grid;
      min-width: 0;

      strong {
        font-weight: 600;
      }

      small {
        justify-self: start;
        color: var(--mat-sys-on-surface-variant);
      }
    }
    .id {
      color: var(--mat-sys-on-surface-variant);
      font-variant-numeric: tabular-nums;
    }
    .row-actions {
      display: inline-flex;
      align-items: center;
      gap: 2px;

      button {
        color: var(--brand-link);
      }

      .danger {
        color: var(--mat-sys-error);
      }

      .good {
        color: var(--on-tint-green);
      }
    }
    .role-select {
      width: 9rem;
      padding-block: var(--space-1);
    }
    td.mat-column-joined {
      white-space: nowrap;
    }
    .filter-group {
      display: grid;
      gap: var(--space-1);

      > span {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-small);
        font-weight: 600;
      }
    }
    .state {
      padding: 0.125rem var(--space-2);
      border-radius: 999px;
      background: var(--tint-green);
      color: var(--on-tint-green);
      font: var(--mat-sys-label-medium);
      white-space: nowrap;

      &.off {
        background: var(--tint-grey);
        color: var(--on-tint-grey);
      }
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
  protected readonly columns = computed(() => ['name', 'nationalId', 'role', 'status', 'joined', ...(this.canAssign() ? ['actions'] : [])]);
  protected readonly activeChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'true', labelKey: 'users.active' },
    { value: 'false', labelKey: 'users.inactive' },
  ];
  protected readonly roleChoices = [
    { value: '', labelKey: 'common.all' },
    ...this.roles.map((role) => ({ value: role, labelKey: `role.${role}` })),
  ];
  protected readonly filters = urlFilters({ search: '', role: '', active: '' });
  protected readonly pageSize = signal(20);

  protected readonly resource = httpResource<Paged<UserSummary>>(() => ({
    url: '/api/v1/users',
    params: {
      page: this.filters.page() + 1,
      pageSize: this.pageSize(),
      ...(this.filters.values.search() ? { search: this.filters.values.search() } : {}),
      ...(this.filters.values.role() ? { role: this.filters.values.role() } : {}),
      ...(this.filters.values.active() ? { active: this.filters.values.active() } : {}),
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

  protected async edit(user: UserSummary): Promise<void> {
    const ref = this.dialog.open(UserEditDialog, { data: user, width: '28rem', maxWidth: 'calc(100vw - 2rem)' });
    const change = await firstValueFrom(ref.afterClosed());

    if (change) {
      await this.run(this.http.put<void>(`/api/v1/users/${user.id}/profile`, change), 'users.saved');
    }
  }

  protected async resetPassword(user: UserSummary): Promise<void> {
    if (!(await this.confirm(user, 'users.resetTitle', 'users.resetMessage', 'users.resetPassword'))) {
      return;
    }

    try {
      const { password } = await firstValueFrom(this.http.post<{ password: string }>(`/api/v1/users/${user.id}/password/reset`, {}));
      this.dialog.open(TemporaryPasswordDialog, { data: password, width: '26rem', maxWidth: 'calc(100vw - 2rem)', disableClose: true });
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  protected async deactivate(user: UserSummary): Promise<void> {
    if (await this.confirm(user, 'users.deactivateTitle', 'users.deactivateMessage', 'users.deactivate')) {
      await this.run(this.http.put<void>(`/api/v1/users/${user.id}/active`, { active: false }), 'users.deactivated');
    }
  }

  protected activate(user: UserSummary): Promise<void> {
    return this.run(this.http.put<void>(`/api/v1/users/${user.id}/active`, { active: true }), 'users.activated');
  }

  private confirm(user: UserSummary, titleKey: string, messageKey: string, confirmKey: string): Promise<boolean | undefined> {
    const name = this.translate.getCurrentLang() === 'ar' ? user.fullName.ar : user.fullName.en;
    const data: ConfirmData = { titleKey, messageKey: this.translate.instant(messageKey, { name }), confirmKey };
    return firstValueFrom(this.dialog.open(ConfirmDialog, { data, width: '26rem', maxWidth: 'calc(100vw - 2rem)' }).afterClosed());
  }

  private async run(request: Observable<void>, doneKey: string): Promise<void> {
    try {
      await firstValueFrom(request);
      this.notify(doneKey);
      this.resource.reload();
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }
}

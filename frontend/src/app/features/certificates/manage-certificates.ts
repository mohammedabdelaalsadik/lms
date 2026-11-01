import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { urlFilters } from '../../core/filters/url-filters';
import { FormsModule } from '@angular/forms';
import { errorKey } from '../../core/http/api-error';
import { Paged } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ChoiceChips } from '../../shared/choice-chips';
import { DateField } from '../../shared/date-field';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { ReasonDialog } from '../../shared/reason-dialog';
import { StatePanel } from '../../shared/state-panel';
import { CERTIFICATES_URL, Certificate, CertificateService } from './certificate.service';

@Component({
  selector: 'app-manage-certificates',
  imports: [DateField, FormsModule, 
    AppDatePipe,
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './manage-certificates.html',
  styles: `
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3) var(--space-4);
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    .main {
      flex: 1 1 18rem;
      min-width: 0;
    }
    small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
    .chip {
      padding: 2px var(--space-2);
      border-radius: var(--radius-s);
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
      font: var(--mat-sys-label-medium);
    }
    .chip.revoked {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
})
export class ManageCertificates {
  private readonly certificates = inject(CertificateService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  protected readonly canRevoke = computed(() => this.auth.hasRole('Admin'));
  protected readonly statusChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'Issued', labelKey: 'certificateStatus.Issued' },
    { value: 'Revoked', labelKey: 'certificateStatus.Revoked' },
  ];
  protected readonly filters = urlFilters({ search: '', status: '', from: '', to: '' });
  protected readonly pageSize = signal(20);

  protected readonly resource = httpResource<Paged<Certificate>>(() => ({
    url: CERTIFICATES_URL,
    params: {
      page: this.filters.page() + 1,
      pageSize: this.pageSize(),
      ...(this.filters.values.search() ? { search: this.filters.values.search() } : {}),
      ...(this.filters.values.status() ? { status: this.filters.values.status() } : {}),
      ...(this.filters.values.from() ? { from: this.filters.values.from() } : {}),
      ...(this.filters.values.to() ? { to: this.filters.values.to() } : {}),
    },
  }));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  protected async download(certificate: Certificate): Promise<void> {
    try {
      await this.certificates.download(certificate);
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  protected async revoke(certificate: Certificate): Promise<void> {
    const ref = this.dialog.open(ReasonDialog, {
      width: '28rem',
      maxWidth: 'calc(100vw - 2rem)',
      data: {
        titleKey: 'certificates.revokeTitle',
        messageKey: 'certificates.revokeMessage',
        labelKey: 'certificates.reason',
        confirmKey: 'certificates.revoke',
      },
    });
    const reason = (await firstValueFrom(ref.afterClosed())) as string | undefined;

    if (reason) {
      await this.run(this.certificates.revoke(certificate.id, reason), 'certificates.revoked');
    }
  }

  protected async reissue(certificate: Certificate): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: {
        titleKey: 'certificates.reissueTitle',
        messageKey: 'certificates.reissueMessage',
        confirmKey: 'certificates.reissue',
      },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.certificates.reissue(certificate.id), 'certificates.reissued');
    }
  }

  private async run(action: Observable<unknown>, successKey: string): Promise<void> {
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

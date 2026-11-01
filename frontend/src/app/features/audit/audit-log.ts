import { HttpClient } from '@angular/common/http';
import { downloadFile } from '../../core/http/download';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { urlFilters } from '../../core/filters/url-filters';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalizedText, Paged } from '../../core/models';
import { DateField } from '../../shared/date-field';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface AuditEntry {
  id: string;
  occurredAt: string;
  actorId: string | null;
  actorName: LocalizedText | null;
  actorRole: string | null;
  ipAddress: string | null;
  action: string;
  details: string;
  succeeded: boolean;
  errorCode: string | null;
}

interface Selected {
  id: string;
  name: LocalizedText | null;
}

@Component({
  selector: 'app-audit-log',
  imports: [
    DateField,
    FilterBar,
    FormsModule,
    LocalizedPipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './audit-log.html',
  styleUrl: './audit-log.scss',
})
export class AuditLog {
  private readonly exportHttp = inject(HttpClient);

  protected exportCsv(): Promise<void> {
    return downloadFile(this.exportHttp, '/api/v1/audit/export', 'audit-log.csv', this.filters.values.search() ? { search: this.filters.values.search() } : {});
  }

  private readonly translate = inject(TranslateService);
  private readonly languages = inject(LanguageService);

  protected readonly filters = urlFilters({ search: '', from: '', to: '' });
  protected readonly actor = signal<Selected | null>(null);
  protected readonly pageSize = signal(20);

  protected readonly resource = httpResource<Paged<AuditEntry>>(() => ({
    url: '/api/v1/audit',
    params: {
      page: this.filters.page() + 1,
      pageSize: this.pageSize(),
      ...(this.filters.values.search() ? { search: this.filters.values.search() } : {}),
      ...(this.filters.values.from() ? { from: this.filters.values.from() } : {}),
      ...(this.filters.values.to() ? { to: this.filters.values.to() } : {}),
      ...(this.actor() ? { actorId: this.actor()!.id } : {}),
    },
  }));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected filterBy(entry: AuditEntry): void {
    this.filters.setPage(0);
    this.actor.set({ id: entry.actorId!, name: entry.actorName });
  }

  protected clearActor(): void {
    this.filters.setPage(0);
    this.actor.set(null);
  }

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  /** New kinds of action appear under their technical name until they are translated. */
  protected actionLabel(action: string): string {
    const key = `auditAction.${action}`;
    const label = this.translate.instant(key);
    return label === key ? action : label;
  }

  protected time(value: string): string {
    const locale = this.languages.language() === 'ar' ? 'ar-SA-u-ca-islamic-umalqura-nu-latn' : 'en-GB';
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value));
  }

  protected reason(entry: AuditEntry): string | null {
    try {
      const details = JSON.parse(entry.details) as { Reason?: string };
      return details.Reason ?? null;
    } catch {
      return null;
    }
  }
}

import { Injectable, OnDestroy, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';

@Injectable()
export class LocalizedPaginatorIntl extends MatPaginatorIntl implements OnDestroy {
  private readonly translate = inject(TranslateService);
  private readonly subscription: Subscription;

  constructor() {
    super();
    this.apply();
    this.subscription = this.translate.onLangChange.subscribe(() => this.apply());
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    const start = length === 0 ? 0 : page * pageSize + 1;
    const end = Math.min((page + 1) * pageSize, length);
    return this.translate.instant('pager.range', { start, end, total: length });
  };

  private apply(): void {
    this.itemsPerPageLabel = this.translate.instant('pager.perPage');
    this.nextPageLabel = this.translate.instant('pager.next');
    this.previousPageLabel = this.translate.instant('pager.previous');
    this.firstPageLabel = this.translate.instant('pager.first');
    this.lastPageLabel = this.translate.instant('pager.last');
    this.changes.next();
  }
}

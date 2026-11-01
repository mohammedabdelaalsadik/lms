import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { MyEnrollment } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

@Component({
  selector: 'app-my-enrollments',
  imports: [AppDatePipe, LocalizedPipe, MatIconModule, StatePanel, TranslatePipe],
  templateUrl: './my-enrollments.html',
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
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    h2 {
      font: var(--mat-sys-title-medium);
      font-weight: 600;
    }
    p {
      margin-block-start: var(--space-1);
      color: var(--mat-sys-on-surface-variant);
    }
    .status {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      font: var(--mat-sys-label-large);
    }
    .status mat-icon {
      width: 18px;
      height: 18px;
      font-size: 18px;
    }
    .Rejected {
      color: var(--mat-sys-error);
    }
    .Enrolled {
      color: var(--mat-sys-primary);
    }
  `,
})
export class MyEnrollments {
  protected readonly icons = {
    Pending: 'hourglass_top',
    Enrolled: 'check_circle',
    Rejected: 'cancel',
    Withdrawn: 'logout',
  };

  protected readonly resource = httpResource<MyEnrollment[]>(() => '/api/v1/my/enrollments');

  protected readonly items = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );
}

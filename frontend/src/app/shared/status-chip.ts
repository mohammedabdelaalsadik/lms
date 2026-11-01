import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { CourseStatus } from '../core/models';

@Component({
  selector: 'app-status-chip',
  imports: [MatIconModule, TranslatePipe],
  template: `
    <span class="chip" [class]="status().toLowerCase()">
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
      {{ 'status.' + status() | translate }}
    </span>
  `,
  styles: `
    .chip {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      padding: 3px 10px;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      white-space: nowrap;
    }
    mat-icon {
      width: 16px;
      height: 16px;
      font-size: 16px;
    }
    .published {
      background: var(--tint-green);
      color: var(--on-tint-green);
    }
    .draft {
      background: var(--tint-amber);
      color: var(--on-tint-amber);
    }
    .archived {
      background: var(--tint-grey);
      color: var(--on-tint-grey);
    }
  `,
})
export class StatusChip {
  readonly status = input.required<CourseStatus>();

  protected readonly icon = computed(
    () =>
      ({ Draft: 'edit_note', Published: 'check_circle', Archived: 'inventory_2' })[this.status()],
  );
}

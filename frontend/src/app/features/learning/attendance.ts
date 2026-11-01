import { httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Component, computed, effect, inject, input, linkedSignal, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { DateField } from '../../shared/date-field';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { AttendanceStatus, CohortAttendance } from './learning.models';
import { LEARNING_URL, LearningService } from './learning.service';

@Component({
  selector: 'app-attendance',
  imports: [
    DateField,
    FormsModule,
    LocalizedPipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './attendance.html',
  styles: `
    .back {
      align-self: flex-start;
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    .date {
      flex: 0 1 16rem;
    }
    .hint {
      flex: 1 1 12rem;
      color: var(--mat-sys-on-surface-variant);
    }
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
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    .who {
      display: grid;
    }
    .who small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class Attendance {
  private readonly learning = inject(LearningService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly statuses: AttendanceStatus[] = ['Present', 'Absent', 'Excused'];
  protected readonly busy = signal(false);
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly date = signal(this.today);
  protected readonly resource = httpResource<CohortAttendance>(() => `${LEARNING_URL}/cohorts/${this.id()}/attendance`);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected readonly sheet = computed(() => this.data()?.sessions.find((s) => s.date === this.date()) ?? null);

  /** Starts from the saved sheet for the date, or everyone present, then follows the staff's choices. */
  protected readonly marks = linkedSignal<Record<string, AttendanceStatus>>(() => {
    const saved = new Map(this.sheet()?.entries.map((e) => [e.traineeId, e.status]));
    return Object.fromEntries((this.data()?.trainees ?? []).map((t) => [t.id, saved.get(t.id) ?? 'Present']));
  });

  constructor() {
    // A cohort that has ended opens on its last day rather than on an invalid date.
    effect(() => {
      const data = this.data();

      if (data && this.date() > data.endsOn) {
        this.date.set(data.endsOn);
      }
    });
  }

  protected mark(traineeId: string, status: AttendanceStatus): void {
    this.marks.update((current) => ({ ...current, [traineeId]: status }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);

    try {
      const entries = Object.entries(this.marks()).map(([traineeId, status]) => ({ traineeId, status }));
      await firstValueFrom(this.learning.saveAttendance(this.id(), this.date(), entries));
      this.notify('attendance.saved');
      this.resource.reload();
    } catch (error) {
      this.notify(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

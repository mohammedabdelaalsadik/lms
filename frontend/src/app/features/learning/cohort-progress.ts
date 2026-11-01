import { HttpClient } from '@angular/common/http';
import { downloadFile } from '../../core/http/download';
import { httpResource } from '@angular/common/http';
import { Component, computed, input, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CohortProgress as Progress } from './learning.models';
import { LEARNING_URL } from './learning.service';

@Component({
  selector: 'app-cohort-progress',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, MatTableModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/cohorts', id()]" class="back">
        <mat-icon class="flip-rtl">arrow_back</mat-icon>
        {{ 'cohorts.roster' | translate }}
      </a>

      <header class="page-header">
        <div>
          <h1>{{ 'progress.title' | translate }}</h1>
          <p>{{ 'progress.subtitle' | translate }}</p>
        </div>
        <button matButton="outlined" (click)="exportCsv()"><mat-icon>download</mat-icon>{{ 'reports.exportCsv' | translate }}</button>
      </header>

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (!data()) {
          <app-state-panel kind="loading" />
        } @else if (data()!.entries.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <div class="scroll">
            <table mat-table [dataSource]="data()!.entries">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>{{ 'fields.name' | translate }}</th>
                <td mat-cell *matCellDef="let row">{{ row.traineeName | localized }}</td>
              </ng-container>
              <ng-container matColumnDef="nationalId">
                <th mat-header-cell *matHeaderCellDef>{{ 'fields.nationalId' | translate }}</th>
                <td mat-cell *matCellDef="let row"><span class="ltr-value">{{ row.maskedNationalId }}</span></td>
              </ng-container>
              <ng-container matColumnDef="percent">
                <th mat-header-cell *matHeaderCellDef>{{ 'learn.progress' | translate }}</th>
                <td mat-cell *matCellDef="let row"><span class="ltr-value">{{ row.percent }}%</span></td>
              </ng-container>
              <ng-container matColumnDef="attendance">
                <th mat-header-cell *matHeaderCellDef>{{ 'attendance.title' | translate }}</th>
                <td mat-cell *matCellDef="let row"><span class="ltr-value">{{ row.attendancePercent }}%</span></td>
              </ng-container>
              <ng-container matColumnDef="completed">
                <th mat-header-cell *matHeaderCellDef>{{ 'learn.completed' | translate }}</th>
                <td mat-cell *matCellDef="let row">{{ row.completedAt ? (row.completedAt | appDate) : '–' }}</td>
              </ng-container>
              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>
          </div>
        }
      </section>
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .scroll {
      overflow-x: auto;
    }
    table {
      min-width: 40rem;
    }
  `,
})
export class CohortProgressPage {
  private readonly exportHttp = inject(HttpClient);

  protected exportCsv(): Promise<void> {
    return downloadFile(this.exportHttp, `/api/v1/learning/cohorts/${this.id()}/progress/export`, 'progress.csv');
  }

  readonly id = input.required<string>();

  protected readonly columns = ['name', 'nationalId', 'percent', 'attendance', 'completed'];
  protected readonly resource = httpResource<Progress>(() => `${LEARNING_URL}/cohorts/${this.id()}/progress`);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
}

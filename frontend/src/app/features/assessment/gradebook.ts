import { HttpClient } from '@angular/common/http';
import { downloadFile } from '../../core/http/download';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { Correction, Gradebook, GradebookItem, GradebookRow } from './assessment.models';
import { AssessmentService, staffUrl } from './assessment.service';
import { GradeDialog, GradeDialogResult } from './grade-dialog';

@Component({
  selector: 'app-gradebook',
  imports: [LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  templateUrl: './gradebook.html',
  styleUrl: './gradebook.scss',
})
export class GradebookPage {
  private readonly exportHttp = inject(HttpClient);

  protected exportCsv(): Promise<void> {
    return downloadFile(this.exportHttp, `/api/v1/assessment/cohorts/${this.id()}/gradebook/export`, 'gradebook.csv');
  }

  private readonly assessment = inject(AssessmentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly canApprove = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly resource = httpResource<Gradebook>(() => `${staffUrl(this.id())}/gradebook`);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly pending = computed(() => this.data()?.corrections.filter((c) => c.status === 'Pending') ?? []);

  protected cell(row: GradebookRow, item: GradebookItem): number | null {
    return row.cells.find((c) => c.itemId === item.id)?.percent ?? null;
  }

  protected async saveCell(row: GradebookRow, item: GradebookItem, raw: string): Promise<void> {
    const text = raw.trim();

    if (text === '' || Number(text) === this.cell(row, item)) {
      return;
    }

    await this.run(this.assessment.setGrade(this.id(), item.id, row.traineeId, Number(text)));
  }

  protected async publish(item: GradebookItem): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'grades.publishTitle', messageKey: 'grades.publishMessage', confirmKey: 'courses.publish' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.assessment.publishGrades(this.id(), item.id), 'grades.published');
    }
  }

  protected async correct(row: GradebookRow, item: GradebookItem): Promise<void> {
    const ref = this.dialog.open(GradeDialog, {
      width: '28rem',
      maxWidth: 'calc(100vw - 2rem)',
      data: { titleKey: 'grades.requestCorrection', textLabelKey: 'grades.reason', textRequired: true, percent: this.cell(row, item), text: null },
    });
    const result = (await firstValueFrom(ref.afterClosed())) as GradeDialogResult | undefined;

    if (result) {
      await this.run(this.assessment.requestCorrection(this.id(), item.id, row.traineeId, result.percent, result.text), 'grades.correctionSent');
    }
  }

  protected decide(correction: Correction, approve: boolean): Promise<void> {
    return this.run(this.assessment.decideCorrection(this.id(), correction.id, approve), approve ? 'grades.corrected' : 'grades.correctionRejected');
  }

  private async run(action: Observable<void>, successKey?: string): Promise<void> {
    try {
      await firstValueFrom(action);

      if (successKey) {
        this.notify(successKey);
      }
    } catch (error) {
      this.notify(errorKey(error));
    }

    this.resource.reload();
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

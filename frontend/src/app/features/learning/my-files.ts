import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { StatePanel } from '../../shared/state-panel';

interface PrivateFile {
  id: string;
  fileName: string;
  size: number;
  uploadedAt: string;
}

interface Files {
  used: number;
  quota: number;
  files: PrivateFile[];
}

const URL_BASE = '/api/v1/my-files';

@Component({
  selector: 'app-my-files',
  imports: [AppDatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'files.title' | translate }}</h1>
          <p>{{ 'files.subtitle' | translate }}</p>
        </div>
        <input #picker hidden type="file" accept=".pdf,.docx,.pptx,.xlsx,.png,.jpg,.jpeg,.mp4" (change)="upload(picker)" />
        <button matButton="filled" [disabled]="busy()" (click)="picker.click()"><mat-icon>upload_file</mat-icon>{{ 'files.upload' | translate }}</button>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <section class="panel usage">
          <small>{{ 'files.used' | translate: { used: size(data()!.used), quota: size(data()!.quota) } }}</small>
          <mat-progress-bar mode="determinate" [value]="(data()!.used / data()!.quota) * 100" />
        </section>

        @if (data()!.files.length === 0) {
          <section class="panel"><app-state-panel kind="empty" /></section>
        } @else {
          <section class="panel">
            @for (file of data()!.files; track file.id) {
              <article>
                <mat-icon aria-hidden="true">description</mat-icon>
                <div>
                  <h2 class="ltr-value">{{ file.fileName }}</h2>
                  <small>{{ size(file.size) }} · {{ file.uploadedAt | appDate }}</small>
                </div>
                <a matButton="outlined" [href]="url(file)" download>{{ 'files.download' | translate }}</a>
                <button matButton [disabled]="busy()" (click)="remove(file)">{{ 'common.remove' | translate }}</button>
              </article>
            }
          </section>
        }
      }
    </div>
  `,
  styles: `
    .usage {
      display: grid;
      gap: var(--space-2);
      padding: var(--space-3) var(--space-4);
    }
    article {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
    }
    article + article {
      border-block-start: 1px solid var(--line);
    }
    article > div {
      flex: 1 1 14rem;
      min-width: 0;
    }
    article mat-icon {
      color: var(--brand-primary);
    }
    h2 {
      display: block;
      margin: 0;
      overflow-wrap: anywhere;
      font: var(--mat-sys-title-small);
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class MyFiles {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<Files>(() => URL_BASE);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected size(bytes: number): string {
    return bytes < 1024 * 1024 ? `${Math.ceil(bytes / 1024)} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected url(file: PrivateFile): string {
    return `${URL_BASE}/${file.id}/download`;
  }

  protected async upload(box: HTMLInputElement): Promise<void> {
    const file = box.files?.[0];
    box.value = '';

    if (file) {
      const body = new FormData();
      body.set('file', file);
      await this.run(this.http.post(URL_BASE, body));
    }
  }

  protected async remove(file: PrivateFile): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'files.removeTitle', messageKey: 'files.removeMessage', confirmKey: 'common.remove' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.http.delete(`${URL_BASE}/${file.id}`));
    }
  }

  private async run(call: Observable<unknown>): Promise<void> {
    this.busy.set(true);

    try {
      await firstValueFrom(call);
      this.resource.reload();
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
    } finally {
      this.busy.set(false);
    }
  }
}

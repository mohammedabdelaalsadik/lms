import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';

interface UploadRow {
  line: number;
  maskedNationalId: string | null;
  nameAr: string;
  nameEn: string;
  email: string;
  role: string | null;
  errorCode: string | null;
}

interface UploadResult {
  valid: number;
  invalid: number;
  rows: UploadRow[];
  created: { line: number; email: string; temporaryPassword: string }[];
}

const URL_BASE = '/api/v1/users/upload';

/** A spreadsheet must not run a cell as a formula when someone opens the file. */
const safeCell = (value: string): string => `"${(/^[=+\-@\t\r]/.test(value) ? `'${value}` : value).replaceAll('"', '""')}"`;

@Component({
  selector: 'app-user-upload',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslatePipe],
  template: `
    <div class="page">
      <a matButton routerLink="/users" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'nav.users' | translate }}</a>

      <header class="page-header">
        <div>
          <h1>{{ 'upload.title' | translate }}</h1>
          <p>{{ 'upload.subtitle' | translate }}</p>
        </div>
      </header>

      @if (error(); as key) {
        <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
      }

      @if (done(); as result) {
        <section class="panel body" role="status">
          <h2>{{ 'upload.doneTitle' | translate: { count: result.created.length } }}</h2>
          <p>{{ 'upload.doneHelp' | translate }}</p>
          <button matButton="filled" (click)="download()"><mat-icon>download</mat-icon>{{ 'upload.download' | translate }}</button>
        </section>
      } @else {
        <section class="panel body">
          <p>{{ 'upload.format' | translate }}</p>
          <code class="ltr-value">national_id,name_ar,name_en,email,role</code>
          <input #file type="file" accept=".csv" hidden (change)="check(file)" />
          <button matButton="outlined" [disabled]="busy()" (click)="file.click()"><mat-icon>upload_file</mat-icon>{{ 'upload.choose' | translate }}</button>
        </section>
      }

      @if (preview(); as result) {
        <section class="panel">
          <div class="summary">
            <span>{{ 'upload.summary' | translate: { valid: result.valid, invalid: result.invalid } }}</span>
            @if (!done() && result.valid > 0) {
              <button matButton="filled" [disabled]="busy()" (click)="save()">{{ 'upload.create' | translate: { count: result.valid } }}</button>
            }
          </div>
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th>#</th>
                  <th>{{ 'fields.nationalId' | translate }}</th>
                  <th>{{ 'fields.name' | translate }}</th>
                  <th>{{ 'fields.email' | translate }}</th>
                  <th>{{ 'fields.role' | translate }}</th>
                  <th>{{ 'upload.result' | translate }}</th>
                </tr>
              </thead>
              <tbody>
                @for (row of result.rows; track row.line) {
                  <tr [class.bad]="row.errorCode">
                    <td>{{ row.line }}</td>
                    <td class="ltr-value">{{ row.maskedNationalId }}</td>
                    <td>{{ row.nameAr }}</td>
                    <td class="ltr-value">{{ row.email }}</td>
                    <td>{{ row.role ? ('role.' + row.role | translate) : '' }}</td>
                    <td>{{ row.errorCode ? (key(row.errorCode) | translate) : ('upload.ok' | translate) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .body {
      display: grid;
      justify-items: start;
      gap: var(--space-3);
      padding: var(--space-4);
    }
    h2,
    p {
      margin: 0;
    }
    code {
      overflow-wrap: anywhere;
    }
    .summary {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      padding: var(--space-3) var(--space-4);
    }
    .scroll {
      overflow-x: auto;
    }
    table {
      width: 100%;
      min-width: 44rem;
      border-collapse: collapse;
    }
    th,
    td {
      padding: var(--space-2) var(--space-3);
      border-block-start: 1px solid var(--line);
      text-align: start;
    }
    tr.bad td:last-child {
      color: var(--mat-sys-error);
    }
  `,
})
export class UserUpload {
  private readonly http = inject(HttpClient);
  private content: Blob | null = null;

  protected readonly preview = signal<UploadResult | null>(null);
  protected readonly done = signal<UploadResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected key(code: string): string {
    return `errors.${code.replaceAll('.', '_')}`;
  }

  protected async check(box: HTMLInputElement): Promise<void> {
    const file = box.files?.[0];
    box.value = '';

    if (file) {
      this.content = file;
      this.preview.set(await this.send(false));
    }
  }

  protected async save(): Promise<void> {
    const result = await this.send(true);

    if (result) {
      this.preview.set(result);
      this.done.set(result);
    }
  }

  protected download(): void {
    const rows = [['email', 'temporary_password'], ...this.done()!.created.map((c) => [c.email, c.temporaryPassword])];
    const text = rows.map((row) => row.map(safeCell).join(',')).join('\r\n');
    const url = URL.createObjectURL(new Blob(['﻿', text], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'new-accounts.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  private async send(save: boolean): Promise<UploadResult | null> {
    const form = new FormData();
    form.set('file', this.content!);
    this.busy.set(true);
    this.error.set(null);

    try {
      return await firstValueFrom(this.http.post<UploadResult>(URL_BASE, form, { params: { save } }));
    } catch (error) {
      this.error.set(errorKey(error));
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}

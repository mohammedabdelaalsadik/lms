import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';

interface RestoreResult {
  courseId: string;
  code: string;
  questions: number;
}

@Component({
  selector: 'app-course-restore',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, RouterLink, TranslatePipe],
  template: `
    <div class="page">
      <a matButton routerLink="/courses" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'nav.courses' | translate }}</a>

      <header class="page-header">
        <div>
          <h1>{{ 'backup.title' | translate }}</h1>
          <p>{{ 'backup.subtitle' | translate }}</p>
        </div>
      </header>

      <form class="panel editor" [formGroup]="form" (ngSubmit)="restore()">
        @if (error(); as key) {
          <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
        }
        <mat-form-field>
          <mat-label>{{ 'backup.code' | translate }}</mat-label>
          <input matInput dir="ltr" maxlength="20" formControlName="code" />
        </mat-form-field>
        <div class="file">
          <input #file type="file" accept=".zip" hidden (change)="choose(file)" />
          <button matButton="outlined" type="button" [disabled]="busy()" (click)="file.click()"><mat-icon>upload_file</mat-icon>{{ 'backup.choose' | translate }}</button>
          <span class="ltr-value">{{ fileName() }}</span>
        </div>
        <div>
          <button matButton="filled" type="submit" [disabled]="busy() || !fileName()">{{ (busy() ? 'backup.working' : 'backup.restore') | translate }}</button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .editor {
      display: grid;
      gap: var(--space-3);
      max-width: 36rem;
      padding: var(--space-4);
    }
    .file {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
    }
  `,
})
export class CourseRestore {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private content: File | null = null;

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fileName = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern('^[A-Za-z0-9-]+$')]],
  });

  protected choose(box: HTMLInputElement): void {
    this.content = box.files?.[0] ?? null;
    this.fileName.set(this.content?.name ?? '');
  }

  protected async restore(): Promise<void> {
    if (this.form.invalid || !this.content || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const body = new FormData();
    body.set('file', this.content);
    body.set('code', this.form.getRawValue().code);

    try {
      const result = await firstValueFrom(this.http.post<RestoreResult>('/api/v1/learning/courses/restore', body));
      await this.router.navigate(['/courses', result.courseId, 'content']);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}

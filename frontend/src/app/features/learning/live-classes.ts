import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { DateField } from '../../shared/date-field';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface LiveClass {
  id: string;
  title: LocalizedText;
  startsAt: string;
  durationMinutes: number;
  status: 'Scheduled' | 'Live' | 'Ended';
  canStart: boolean;
  attendees: { name: LocalizedText; joinedAt: string }[];
}

interface LiveList {
  isConfigured: boolean;
  canManage: boolean;
  cohortName: LocalizedText;
  classes: LiveClass[];
}

@Component({
  selector: 'app-live-classes',
  imports: [AppDatePipe, DateField, LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'live.title' | translate }}</h1>
          <p>{{ data()?.cohortName | localized }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        @if (!data()!.isConfigured) {
          <p class="notice" role="status"><mat-icon aria-hidden="true">info</mat-icon>{{ 'live.notConnected' | translate }}</p>
        }

        @if (data()!.canManage) {
          <form class="panel editor" [formGroup]="form" (ngSubmit)="schedule()">
            <mat-form-field subscriptSizing="dynamic"><mat-label>{{ 'live.titleAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="200" formControlName="titleAr" /></mat-form-field>
            <mat-form-field subscriptSizing="dynamic"><mat-label>{{ 'live.titleEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="200" formControlName="titleEn" /></mat-form-field>
            <app-date-field formControlName="startsAt" [time]="true" [label]="'live.startsAt' | translate" />
            <mat-form-field subscriptSizing="dynamic"><mat-label>{{ 'live.minutes' | translate }}</mat-label><input matInput type="number" min="15" max="480" formControlName="durationMinutes" /></mat-form-field>
            <div><button matButton="filled" type="submit" [disabled]="busy()">{{ 'live.schedule' | translate }}</button></div>
          </form>
        }

        @if (data()!.classes.length === 0) {
          <section class="panel"><app-state-panel kind="empty" /></section>
        } @else {
          <section class="panel">
            @for (item of data()!.classes; track item.id) {
              <article>
                <div class="info">
                  <h2>{{ item.title | localized }}</h2>
                  <small>{{ item.startsAt | appDate }} · {{ time(item.startsAt) }} · {{ 'live.length' | translate: { minutes: item.durationMinutes } }}</small>
                  @if (item.attendees.length) {
                    <small>{{ 'live.attended' | translate: { count: item.attendees.length } }}: @for (person of item.attendees; track $index) {<span class="person">{{ person.name | localized }}</span>}</small>
                  }
                </div>
                <span class="state" [class]="item.status.toLowerCase()">{{ 'live.status' + item.status | translate }}</span>
                <div class="actions">
                  @if (data()!.canManage) {
                    @if (item.canStart) {
                      <button matButton="filled" [disabled]="busy()" (click)="enter(item)">{{ (item.status === 'Live' ? 'live.rejoin' : 'live.start') | translate }}</button>
                    }
                    @if (item.status === 'Live') {
                      <button matButton="outlined" [disabled]="busy()" (click)="end(item)">{{ 'live.end' | translate }}</button>
                    }
                    @if (item.status === 'Scheduled') {
                      <button matButton [disabled]="busy()" (click)="cancel(item)">{{ 'live.cancel' | translate }}</button>
                    }
                  } @else if (item.status === 'Live') {
                    <button matButton="filled" [disabled]="busy()" (click)="enter(item)">{{ 'live.join' | translate }}</button>
                  }
                </div>
              </article>
            }
          </section>
        }
      }
    </div>
  `,
  styles: `
    .notice {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
      padding: var(--space-3);
      border-radius: var(--radius-s);
      background: var(--tint-amber);
      color: var(--on-tint-amber);
    }
    .editor {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: var(--space-3);
      align-items: center;
      padding: var(--space-4);
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
    .info {
      flex: 1 1 16rem;
      min-width: 0;
    }
    h2 {
      margin: 0;
      font: var(--mat-sys-title-small);
    }
    small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
    .person {
      margin-inline-start: var(--space-2);
    }
    .state {
      padding: 3px 10px;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
    }
    .scheduled {
      background: var(--tint-blue);
      color: var(--on-tint-blue);
    }
    .live {
      background: var(--tint-green);
      color: var(--on-tint-green);
    }
    .ended {
      background: var(--tint-grey);
      color: var(--on-tint-grey);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class LiveClasses {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly cohortId = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<LiveList>(() => this.url());
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly form = inject(FormBuilder).nonNullable.group({
    titleAr: ['', Validators.required],
    titleEn: ['', Validators.required],
    startsAt: ['', Validators.required],
    durationMinutes: [60, [Validators.required, Validators.min(15), Validators.max(480)]],
  });

  private url(): string {
    return `/api/v1/learning/cohorts/${this.cohortId()}/live`;
  }

  protected time(value: string): string {
    return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  protected async schedule(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const done = await this.run(this.http.post(this.url(), { ...value, startsAt: new Date(value.startsAt).toISOString() }));

    if (done !== null) {
      this.form.reset();
    }
  }

  protected async enter(item: LiveClass): Promise<void> {
    const link = await this.run(this.http.post<{ url: string }>(`${this.url()}/${item.id}/join`, {}));

    if (link && link !== true) {
      window.location.assign(link.url);
    }
  }

  protected async end(item: LiveClass): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'live.endTitle', messageKey: 'live.endMessage', confirmKey: 'live.end' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.http.post(`${this.url()}/${item.id}/end`, {}));
    }
  }

  protected cancel(item: LiveClass): Promise<unknown> {
    return this.run(this.http.delete(`${this.url()}/${item.id}`));
  }

  private async run<T>(call: Observable<T>): Promise<T | true | null> {
    this.busy.set(true);

    try {
      const value = await firstValueFrom(call);
      this.resource.reload();
      return value ?? true;
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}

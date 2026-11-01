import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

type Status = 'Submission' | 'Review' | 'Closed';

interface Entry {
  trainee: LocalizedText | null;
  text: string;
  reviewsDone: number;
  reviewsAssigned: number;
  average: number | null;
}

interface StaffWorkshop {
  id: string;
  title: LocalizedText;
  instructions: LocalizedText;
  status: Status;
  reviewsPerPerson: number;
  entries: Entry[];
}

interface ReviewTask {
  id: string;
  text: string;
  score: number | null;
  comment: string | null;
}

interface MyWorkshop {
  id: string;
  title: LocalizedText;
  instructions: LocalizedText;
  status: Status;
  myText: string | null;
  toReview: ReviewTask[];
  received: { score: number | null; comment: string | null }[];
}

const STYLES = `
  .back {
    align-self: flex-start;
  }
  .card {
    display: grid;
    gap: var(--space-3);
    padding: var(--space-4);
  }
  .editor {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
    gap: var(--space-2) var(--space-3);
    padding: var(--space-4);
  }
  .form-error {
    grid-column: 1 / -1;
  }
  h2,
  h3,
  p,
  blockquote {
    margin: 0;
  }
  h2 {
    font: var(--mat-sys-title-medium);
  }
  h3 {
    font: var(--mat-sys-title-small);
  }
  p,
  small {
    color: var(--mat-sys-on-surface-variant);
  }
  blockquote {
    padding-inline-start: var(--space-3);
    border-inline-start: 3px solid var(--mat-sys-outline-variant);
    white-space: pre-wrap;
    overflow-wrap: anywhere;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-2);
  }
  .task {
    display: grid;
    gap: var(--space-2);
    padding-block-start: var(--space-3);
    border-block-start: 1px solid var(--mat-sys-outline-variant);
  }
  .grow {
    flex: 1 1 14rem;
  }
  .score {
    flex: 0 1 10rem;
  }
  mat-form-field {
    width: 100%;
  }
`;

@Component({
  selector: 'app-staff-workshops',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/cohorts', id()]" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'cohorts.roster' | translate }}</a>
      <header class="page-header">
        <div>
          <h1>{{ 'workshops.title' | translate }}</h1>
          <p>{{ 'workshops.subtitle' | translate }}</p>
        </div>
      </header>

      <form class="panel editor" [formGroup]="form" (ngSubmit)="create()">
        <mat-form-field><mat-label>{{ 'workshops.titleAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="200" formControlName="titleAr" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'workshops.titleEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="200" formControlName="titleEn" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'workshops.instructionsAr' | translate }}</mat-label><textarea matInput dir="rtl" rows="2" maxlength="4000" formControlName="instructionsAr"></textarea></mat-form-field>
        <mat-form-field><mat-label>{{ 'workshops.instructionsEn' | translate }}</mat-label><textarea matInput dir="ltr" rows="2" maxlength="4000" formControlName="instructionsEn"></textarea></mat-form-field>
        <mat-form-field><mat-label>{{ 'workshops.reviews' | translate }}</mat-label><input matInput type="number" min="1" max="5" formControlName="reviewsPerPerson" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'workshops.weight' | translate }}</mat-label><input matInput type="number" min="1" formControlName="weight" /></mat-form-field>
        <div><button matButton="filled" type="submit" [disabled]="busy()">{{ 'workshops.create' | translate }}</button></div>
      </form>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (items()?.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        @for (workshop of items(); track workshop.id) {
          <section class="panel card">
            <div>
              <h2>{{ workshop.title | localized }}</h2>
              <small>{{ 'workshops.status' + workshop.status | translate }}</small>
            </div>
            <p>{{ workshop.instructions | localized }}</p>
            @for (entry of workshop.entries; track $index) {
              <div class="task">
                <h3>{{ entry.trainee | localized }}</h3>
                <blockquote>{{ entry.text }}</blockquote>
                <small>
                  {{ 'workshops.reviewsDone' | translate: { done: entry.reviewsDone, total: entry.reviewsAssigned } }}
                  @if (entry.average !== null) {
                    · {{ 'workshops.average' | translate: { value: entry.average } }}
                  }
                </small>
              </div>
            }
            @if (workshop.status === 'Submission') {
              <div><button matButton="filled" [disabled]="busy()" (click)="advance(workshop)">{{ 'workshops.startReviews' | translate }}</button></div>
            } @else if (workshop.status === 'Review') {
              <small>{{ 'workshops.closeNote' | translate }}</small>
              <div><button matButton="filled" [disabled]="busy()" (click)="advance(workshop)">{{ 'workshops.close' | translate }}</button></div>
            }
          </section>
        }
      }
    </div>
  `,
  styles: STYLES,
})
export class StaffWorkshops {
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<StaffWorkshop[]>(() => this.url());
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly form = inject(FormBuilder).nonNullable.group({
    titleAr: ['', Validators.required],
    titleEn: ['', Validators.required],
    instructionsAr: ['', Validators.required],
    instructionsEn: ['', Validators.required],
    reviewsPerPerson: [2, [Validators.required, Validators.min(1), Validators.max(5)]],
    weight: [10, [Validators.required, Validators.min(1)]],
  });

  private url(): string {
    return `/api/v1/assessment/cohorts/${this.id()}/workshops`;
  }

  protected async create(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (await this.run(this.http.post(this.url(), this.form.getRawValue()))) {
      this.form.reset();
    }
  }

  protected advance(workshop: StaffWorkshop): Promise<boolean> {
    return this.run(this.http.post(`${this.url()}/${workshop.id}/advance`, {}));
  }

  private async run(call: Observable<unknown>): Promise<boolean> {
    this.busy.set(true);

    try {
      await firstValueFrom(call);
      this.resource.reload();
      return true;
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'app-my-workshops',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/learn', cohortId()]" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'nav.learn' | translate }}</a>
      <header class="page-header">
        <div>
          <h1>{{ 'workshops.title' | translate }}</h1>
          <p>{{ 'workshops.mySubtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!items()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else if (items()!.length === 0) {
        <section class="panel"><app-state-panel kind="empty" /></section>
      } @else {
        @for (workshop of items(); track workshop.id) {
          <section class="panel card">
            <div>
              <h2>{{ workshop.title | localized }}</h2>
              <small>{{ 'workshops.status' + workshop.status | translate }}</small>
            </div>
            <p>{{ workshop.instructions | localized }}</p>

            <h3>{{ 'workshops.yourWork' | translate }}</h3>
            @if (workshop.status === 'Submission') {
              <mat-form-field subscriptSizing="dynamic">
                <textarea #work matInput rows="6" maxlength="20000" [attr.aria-label]="'workshops.yourWork' | translate" [value]="workshop.myText ?? ''"></textarea>
              </mat-form-field>
              <div><button matButton="filled" [disabled]="busy()" (click)="submit(workshop, work.value)">{{ 'workshops.submit' | translate }}</button></div>
            } @else if (workshop.myText) {
              <blockquote>{{ workshop.myText }}</blockquote>
            }

            @if (workshop.toReview.length) {
              <h3>{{ 'workshops.toReview' | translate }}</h3>
              @for (task of workshop.toReview; track task.id) {
                <div class="task">
                  <blockquote>{{ task.text }}</blockquote>
                  <div class="row">
                    <mat-form-field class="score" subscriptSizing="dynamic">
                      <mat-label>{{ 'workshops.score' | translate }}</mat-label>
                      <input #score matInput type="number" min="0" max="100" [value]="task.score ?? ''" />
                    </mat-form-field>
                    <mat-form-field class="grow" subscriptSizing="dynamic">
                      <mat-label>{{ 'workshops.comment' | translate }}</mat-label>
                      <input #comment matInput maxlength="2000" [value]="task.comment ?? ''" />
                    </mat-form-field>
                    <button matButton="outlined" [disabled]="busy()" (click)="review(task, score.value, comment.value)">{{ 'workshops.saveReview' | translate }}</button>
                    @if (task.score !== null) {
                      <small>{{ 'workshops.saved' | translate }}</small>
                    }
                  </div>
                </div>
              }
            }

            @if (workshop.received.length) {
              <h3>{{ 'workshops.received' | translate }}</h3>
              @for (item of workshop.received; track $index) {
                <div class="task">
                  @if (item.comment) {
                    <blockquote>{{ item.comment }}</blockquote>
                  }
                  <small>{{ item.score ?? ('workshops.scoreHidden' | translate) }}</small>
                </div>
              }
            }
          </section>
        }
      }
    </div>
  `,
  styles: STYLES,
})
export class MyWorkshops {
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly cohortId = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<MyWorkshop[]>(() => `${this.url()}/workshops`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  private url(): string {
    return `/api/v1/assessment/my/cohorts/${this.cohortId()}`;
  }

  protected submit(workshop: MyWorkshop, text: string): Promise<void> {
    return this.run(this.http.put(`${this.url()}/workshops/${workshop.id}/submission`, { text }));
  }

  protected review(task: ReviewTask, score: string, comment: string): Promise<void> {
    return this.run(this.http.put(`${this.url()}/workshop-reviews/${task.id}`, { score: score === '' ? -1 : Number(score), comment }));
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

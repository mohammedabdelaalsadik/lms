import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface Entry {
  id: string;
  term: LocalizedText;
  definition: LocalizedText;
  isPending: boolean;
  isMine: boolean;
}

interface Glossary {
  canManage: boolean;
  entries: Entry[];
}

@Component({
  selector: 'app-glossary-page',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'glossary.title' | translate }}</h1>
          <p>{{ 'glossary.subtitle' | translate }}</p>
        </div>
      </header>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <form class="panel editor" [formGroup]="form" (ngSubmit)="save()">
          @if (error(); as key) {
            <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
          }
          <mat-form-field><mat-label>{{ 'glossary.termAr' | translate }}</mat-label><input matInput dir="rtl" maxlength="200" formControlName="termAr" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'glossary.termEn' | translate }}</mat-label><input matInput dir="ltr" maxlength="200" formControlName="termEn" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'glossary.definitionAr' | translate }}</mat-label><textarea matInput dir="rtl" rows="2" maxlength="4000" formControlName="definitionAr"></textarea></mat-form-field>
          <mat-form-field><mat-label>{{ 'glossary.definitionEn' | translate }}</mat-label><textarea matInput dir="ltr" rows="2" maxlength="4000" formControlName="definitionEn"></textarea></mat-form-field>
          <div class="actions">
            <button matButton="filled" type="submit" [disabled]="busy()">{{ (editing() ? 'glossary.update' : data()!.canManage ? 'glossary.add' : 'glossary.suggest') | translate }}</button>
            @if (editing()) {
              <button matButton type="button" (click)="reset()">{{ 'common.cancel' | translate }}</button>
            }
          </div>
        </form>

        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'glossary.search' | translate }}</mat-label>
          <input matInput (input)="search.set($any($event.target).value)" />
        </mat-form-field>

        @if (shown().length === 0) {
          <section class="panel"><app-state-panel kind="empty" /></section>
        } @else {
          <section class="panel list">
            @for (entry of shown(); track entry.id) {
              <article>
                <div>
                  <h2>{{ entry.term | localized }}</h2>
                  <p>{{ entry.definition | localized }}</p>
                  @if (entry.isPending) {
                    <small>{{ 'glossary.pending' | translate }}</small>
                  }
                </div>
                <div class="actions">
                  @if (data()!.canManage && entry.isPending) {
                    <button matButton="outlined" [disabled]="busy()" (click)="approve(entry)">{{ 'glossary.approve' | translate }}</button>
                  }
                  @if (data()!.canManage || (entry.isMine && entry.isPending)) {
                    <button matButton (click)="edit(entry)">{{ 'common.edit' | translate }}</button>
                    <button matButton [disabled]="busy()" (click)="remove(entry)">{{ 'common.remove' | translate }}</button>
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
    .editor {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
      gap: var(--space-2) var(--space-3);
      padding: var(--space-4);
    }
    .form-error,
    .editor .actions {
      grid-column: 1 / -1;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    article {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
    }
    article + article {
      border-block-start: 1px solid var(--mat-sys-outline-variant);
    }
    article > div:first-child {
      flex: 1 1 18rem;
      min-width: 0;
    }
    h2,
    p {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-small);
    }
    p,
    small {
      color: var(--mat-sys-on-surface-variant);
      white-space: pre-wrap;
    }
  `,
})
export class GlossaryPage {
  private readonly http = inject(HttpClient);

  readonly courseId = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly editing = signal<string | null>(null);
  protected readonly resource = httpResource<Glossary>(() => this.url());
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();
    const entries = this.data()?.entries ?? [];
    return term ? entries.filter((e) => [e.term.ar, e.term.en, e.definition.ar, e.definition.en].some((t) => t.toLowerCase().includes(term))) : entries;
  });
  protected readonly form = inject(FormBuilder).nonNullable.group({
    termAr: ['', Validators.required],
    termEn: ['', Validators.required],
    definitionAr: ['', Validators.required],
    definitionEn: ['', Validators.required],
  });

  private url(): string {
    return `/api/v1/learning/courses/${this.courseId()}/glossary`;
  }

  protected edit(entry: Entry): void {
    this.editing.set(entry.id);
    this.form.setValue({ termAr: entry.term.ar, termEn: entry.term.en, definitionAr: entry.definition.ar, definitionEn: entry.definition.en });
  }

  protected reset(): void {
    this.editing.set(null);
    this.form.reset();
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const id = this.editing();
    const body = this.form.getRawValue();

    if (await this.run(id ? this.http.put(`${this.url()}/${id}`, body) : this.http.post(this.url(), body))) {
      this.reset();
    }
  }

  protected approve(entry: Entry): Promise<boolean> {
    return this.run(this.http.post(`${this.url()}/${entry.id}/approve`, {}));
  }

  protected remove(entry: Entry): Promise<boolean> {
    return this.run(this.http.delete(`${this.url()}/${entry.id}`));
  }

  private async run(call: Observable<unknown>): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(call);
      this.resource.reload();
      return true;
    } catch (error) {
      this.error.set(errorKey(error));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}

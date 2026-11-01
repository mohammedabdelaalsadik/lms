import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface Summary {
  id: string;
  title: string;
  version: number;
  updatedAt: string;
}

interface WikiList {
  cohortName: LocalizedText;
  canModerate: boolean;
  pages: Summary[];
}

interface Detail {
  id: string;
  title: string;
  body: string;
  version: number;
  canModerate: boolean;
  history: { version: number; editedBy: LocalizedText | null; editedAt: string }[];
}

interface Old {
  version: number;
  title: string;
  body: string;
}

@Component({
  selector: 'app-wiki-page',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'wiki.title' | translate }}</h1>
          <p>{{ list()?.cohortName | localized }} · {{ 'wiki.subtitle' | translate }}</p>
        </div>
        <button matButton="filled" (click)="startNew()"><mat-icon>add</mat-icon>{{ 'wiki.newPage' | translate }}</button>
      </header>

      @if (error(); as key) {
        <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
      }

      @if (listResource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="listResource.reload()" /></section>
      } @else {
        <div class="layout">
          <nav class="panel pages" [attr.aria-label]="'wiki.pages' | translate">
            @for (page of list()?.pages; track page.id) {
              <button matButton [class.current]="page.id === selected()" (click)="open(page.id)">{{ page.title }}</button>
            } @empty {
              <p class="none">{{ 'wiki.pages' | translate }}: 0</p>
            }
          </nav>

          <article class="panel content">
            @if (composing()) {
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ 'wiki.pageTitle' | translate }}</mat-label>
                <input #title matInput maxlength="200" [value]="draftTitle()" />
              </mat-form-field>
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ 'wiki.body' | translate }}</mat-label>
                <textarea #body matInput rows="14" maxlength="50000" [value]="draftBody()"></textarea>
              </mat-form-field>
              <div class="actions">
                <button matButton="filled" [disabled]="busy()" (click)="save(title.value, body.value)">{{ 'common.save' | translate }}</button>
                <button matButton (click)="composing.set(false)">{{ 'common.cancel' | translate }}</button>
              </div>
            } @else if (detail(); as page) {
              @if (old(); as earlier) {
                <p class="viewing">
                  {{ 'wiki.viewing' | translate }} · {{ 'wiki.version' | translate: { version: earlier.version } }}
                  <button matButton (click)="old.set(null)">{{ 'wiki.backToLatest' | translate }}</button>
                  @if (page.canModerate) {
                    <button matButton="outlined" [disabled]="busy()" (click)="restore(earlier.version)">{{ 'wiki.restore' | translate }}</button>
                  }
                </p>
              }
              <h2>{{ old()?.title ?? page.title }}</h2>
              <p class="text">{{ old()?.body ?? page.body }}</p>
              @if (!old()) {
                <div class="actions">
                  <button matButton="outlined" (click)="startEdit(page)">{{ 'common.edit' | translate }}</button>
                  @if (page.canModerate) {
                    <button matButton [disabled]="busy()" (click)="remove(page)">{{ 'common.remove' | translate }}</button>
                  }
                </div>
              }
              <h3>{{ 'wiki.history' | translate }}</h3>
              <ol>
                @for (entry of page.history; track entry.version) {
                  <li>
                    <button matButton (click)="view(entry.version)">{{ 'wiki.version' | translate: { version: entry.version } }}</button>
                    <small>{{ entry.editedBy | localized }} · {{ entry.editedAt | appDate }}</small>
                  </li>
                }
              </ol>
            } @else {
              <app-state-panel kind="empty" />
            }
          </article>
        </div>
      }
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(12rem, 16rem) minmax(0, 1fr);
      gap: var(--space-4);
      align-items: start;
    }
    @media (max-width: 48rem) {
      .layout {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .pages {
      display: grid;
      padding: var(--space-2);

      button {
        justify-content: start;
      }
    }
    .current {
      background: var(--mat-sys-surface-container);
    }
    .content {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
      min-width: 0;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    h2,
    h3,
    .text,
    .none,
    .viewing,
    ol {
      margin: 0;
    }
    h2 {
      font: var(--mat-sys-title-large);
    }
    h3 {
      font: var(--mat-sys-title-small);
    }
    .text {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    .none,
    small,
    .viewing {
      color: var(--mat-sys-on-surface-variant);
    }
    ol {
      padding: 0;
      list-style: none;
    }
  `,
})
export class WikiPage {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);

  readonly cohortId = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<string | null>(null);
  protected readonly composing = signal(false);
  protected readonly draftTitle = signal('');
  protected readonly draftBody = signal('');
  protected readonly old = signal<Old | null>(null);
  private readonly basedOn = signal<number | null>(null);
  protected readonly listResource = httpResource<WikiList>(() => this.url());
  protected readonly list = computed(() => (this.listResource.hasValue() ? this.listResource.value() : null));
  protected readonly pageResource = httpResource<Detail>(() => (this.selected() ? `${this.url()}/${this.selected()}` : undefined));
  protected readonly detail = computed(() => (this.selected() && this.pageResource.hasValue() ? this.pageResource.value() : null));

  private url(): string {
    return `/api/v1/wikis/${this.cohortId()}/pages`;
  }

  protected open(id: string): void {
    this.composing.set(false);
    this.old.set(null);
    this.selected.set(id);
  }

  protected startNew(): void {
    this.basedOn.set(null);
    this.draftTitle.set('');
    this.draftBody.set('');
    this.composing.set(true);
  }

  protected startEdit(page: Detail): void {
    this.basedOn.set(page.version);
    this.draftTitle.set(page.title);
    this.draftBody.set(page.body);
    this.composing.set(true);
  }

  protected async save(title: string, body: string): Promise<void> {
    const version = this.basedOn();
    const id = this.selected();
    const call = version === null ? this.http.post<string>(this.url(), { title, body }) : this.http.put(`${this.url()}/${id}`, { title, body, version });
    const result = await this.run(call);

    if (result === null) {
      return;
    }

    this.composing.set(false);

    if (typeof result === 'string') {
      this.selected.set(result);
    }
  }

  protected async view(version: number): Promise<void> {
    const found = await this.run(this.http.get<Old>(`${this.url()}/${this.selected()}/revisions/${version}`));
    this.old.set(found as Old | null);
  }

  protected async restore(version: number): Promise<void> {
    if ((await this.run(this.http.post(`${this.url()}/${this.selected()}/revisions/${version}/restore`, {}))) !== null) {
      this.old.set(null);
    }
  }

  protected async remove(page: Detail): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'common.remove', messageKey: 'wiki.subtitle', confirmKey: 'common.remove' },
    });

    if ((await firstValueFrom(ref.afterClosed())) && (await this.run(this.http.delete(`${this.url()}/${page.id}`))) !== null) {
      this.selected.set(null);
    }
  }

  private async run<T>(call: Observable<T>): Promise<T | true | null> {
    this.busy.set(true);
    this.error.set(null);

    try {
      const value = await firstValueFrom(call);
      this.listResource.reload();
      this.pageResource.reload();
      return value ?? true;
    } catch (error) {
      this.error.set(errorKey(error));
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}

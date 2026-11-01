import { HttpClient, httpResource } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { urlFilters } from '../../core/filters/url-filters';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CommunityService, Contact, ConversationSummary, MESSAGES_URL, Thread } from './community.service';

const REFRESH_MS = 15_000;

@Component({
  selector: 'app-messages-page',
  imports: [
    AppDatePipe,
    FormsModule,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'messages.title' | translate }}</h1>
          <p>{{ 'messages.subtitle' | translate }}</p>
        </div>
      </header>

      <section class="panel layout" [class.has-thread]="withUser()">
        <aside>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'messages.findPerson' | translate }}</mat-label>
            <input matInput maxlength="100" name="find" [ngModel]="term()" (ngModelChange)="term.set($event)" />
          </mat-form-field>

          @if (term().trim()) {
            <ul>
              @for (person of contacts(); track person.id) {
                <li>
                  <button type="button" class="row" (click)="open(person.id)">
                    <span class="text">
                      <strong>{{ person.name | localized }}</strong>
                      <small>{{ 'role.' + person.role | translate }}</small>
                    </span>
                  </button>
                </li>
              } @empty {
                <li class="note">{{ 'common.empty' | translate }}</li>
              }
            </ul>
          } @else if (conversations.error()) {
            <app-state-panel kind="error" (retry)="conversations.reload()" />
          } @else if (!list()) {
            <app-state-panel kind="loading" />
          } @else {
            <ul>
              @for (item of list(); track item.userId) {
                <li>
                  <button type="button" class="row" [class.current]="item.userId === withUser()" (click)="open(item.userId)">
                    <span class="text">
                      <strong>{{ item.name | localized }}</strong>
                      <small class="preview">{{ item.lastMessage }}</small>
                    </span>
                    @if (item.unread > 0) {
                      <span class="count" [attr.aria-label]="'messages.unread' | translate: { count: item.unread }">{{ item.unread }}</span>
                    }
                  </button>
                </li>
              } @empty {
                <li class="note">{{ 'messages.none' | translate }}</li>
              }
            </ul>
          }
        </aside>

        <div class="thread">
          @if (!withUser()) {
            <p class="note hint">{{ 'messages.pick' | translate }}</p>
          } @else if (thread.error()) {
            <app-state-panel kind="error" (retry)="thread.reload()" />
          } @else if (!current()) {
            <app-state-panel kind="loading" />
          } @else {
            <header>
              <button matIconButton class="close" [attr.aria-label]="'messages.back' | translate" (click)="open('')">
                <mat-icon class="flip-rtl">arrow_back</mat-icon>
              </button>
              <strong>{{ current()!.name | localized }}</strong>
              <small>{{ 'role.' + current()!.role | translate }}</small>
            </header>

            <ol aria-live="polite">
              @for (message of current()!.messages; track message.id) {
                <li [class.mine]="message.isMine">
                  <p>{{ message.body }}</p>
                  <small>
                    {{ message.sentAt | appDate: true }}
                    @if (message.isMine && message.isRead) {
                      · {{ 'messages.read' | translate }}
                    }
                  </small>
                </li>
              }
            </ol>

            <form (ngSubmit)="send()">
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ 'messages.write' | translate }}</mat-label>
                <textarea matInput name="draft" rows="2" maxlength="4000" [ngModel]="draft()" (ngModelChange)="draft.set($event)"></textarea>
              </mat-form-field>
              <button type="submit" matButton="filled" [disabled]="busy() || !draft().trim()">
                <mat-icon class="flip-rtl">send</mat-icon>
                {{ 'messages.send' | translate }}
              </button>
            </form>
          }
        </div>
      </section>
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(16rem, 20rem) 1fr;
      min-height: 32rem;
    }
    aside {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      padding: var(--space-3);
      border-inline-end: 1px solid var(--line);
      min-width: 0;
    }
    ul,
    ol {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .panel ul > li,
    .panel ol > li {
      border: 0;
    }
    .row {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      width: 100%;
      padding: var(--space-2);
      border: 0;
      border-radius: var(--radius-s);
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;

      &:hover,
      &.current {
        background: var(--mat-sys-surface-container);
      }
    }
    .text {
      display: grid;
      flex: 1;
      gap: 2px;
      min-width: 0;
    }
    strong {
      font-weight: 500;
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
    .preview {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .count {
      display: grid;
      flex: none;
      place-items: center;
      min-width: 1.375rem;
      height: 1.375rem;
      padding-inline: 4px;
      border-radius: 11px;
      background: var(--brand-primary);
      color: var(--mat-sys-on-primary);
      font-size: 0.75rem;
    }
    .note {
      padding: var(--space-3);
      color: var(--mat-sys-on-surface-variant);
    }
    .hint {
      margin: auto;
    }
    .thread {
      display: flex;
      flex-direction: column;
      min-width: 0;

      header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        min-height: 3.25rem;
        padding-inline: var(--space-4);
        border-block-end: 1px solid var(--line);
      }

      form {
        display: flex;
        align-items: flex-end;
        gap: var(--space-2);
        padding: var(--space-3);
        border-block-start: 1px solid var(--line);

        mat-form-field {
          flex: 1;
        }
      }
    }
    .close {
      display: none;
    }
    ol {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: var(--space-2);
      max-height: 28rem;
      padding: var(--space-4);
      overflow-y: auto;

      li {
        align-self: flex-start;
        max-width: min(80%, 34rem);
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-m);
        background: var(--mat-sys-surface-container);
      }

      li.mine {
        align-self: flex-end;
        background: var(--mat-sys-primary-container);
        color: var(--mat-sys-on-primary-container);
      }

      p {
        margin: 0;
        overflow-wrap: anywhere;
        white-space: pre-wrap;
      }
    }
    @media (max-width: 767px) {
      .layout {
        grid-template-columns: 1fr;
      }
      aside {
        border-inline-end: 0;
      }
      .layout.has-thread aside,
      .layout:not(.has-thread) .thread {
        display: none;
      }
      .close {
        display: inline-flex;
      }
    }
  `,
})
export class MessagesPage implements OnInit, OnDestroy {
  private readonly community = inject(CommunityService);
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private timer?: ReturnType<typeof setInterval>;

  private readonly filters = urlFilters({ with: '' });

  protected readonly withUser = this.filters.values.with;
  protected readonly term = signal('');
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly contacts = signal<Contact[]>([]);

  protected readonly conversations = httpResource<ConversationSummary[]>(() => MESSAGES_URL);
  protected readonly list = computed(() => (this.conversations.hasValue() ? this.conversations.value() : null));
  protected readonly thread = httpResource<Thread>(() => (this.withUser() ? `${MESSAGES_URL}/${this.withUser()}` : undefined));
  protected readonly current = computed(() => (this.thread.hasValue() ? this.thread.value() : null));

  constructor() {
    effect(() => void this.findContacts(this.term()));

    // Opening a thread clears its unread count, here and on the badge.
    effect(() => {
      const unread = this.list()?.find((item) => item.userId === this.withUser())?.unread ?? 0;

      if (unread > 0) {
        void this.community.markRead(this.withUser()).then(() => this.conversations.reload());
      }
    });
  }

  ngOnInit(): void {
    this.timer = setInterval(() => {
      if (!document.hidden) {
        this.conversations.reload();
        this.thread.reload();
      }
    }, REFRESH_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected open(userId: string): void {
    this.term.set('');
    this.draft.set('');
    this.filters.set('with', userId);
  }

  protected async send(): Promise<void> {
    this.busy.set(true);

    try {
      await this.community.send(this.withUser(), this.draft().trim());
      this.draft.set('');
      this.thread.reload();
      this.conversations.reload();
    } catch {
      this.snackBar.open(this.translate.instant('community.sendFailed'), undefined, { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }

  private async findContacts(term: string): Promise<void> {
    if (!term.trim()) {
      this.contacts.set([]);
      return;
    }

    try {
      const found = await firstValueFrom(this.http.get<Contact[]>(`${MESSAGES_URL}/contacts`, { params: { search: term.trim() } }));

      if (term === this.term()) {
        this.contacts.set(found);
      }
    } catch {
      this.contacts.set([]);
    }
  }
}

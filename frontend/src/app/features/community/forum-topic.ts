import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CommunityService, FORUMS_URL, TopicDetail } from './community.service';

@Component({
  selector: 'app-forum-topic',
  imports: [
    AppDatePipe,
    FormsModule,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/forum', cohortId()]" class="back">
        <mat-icon class="flip-rtl">arrow_back</mat-icon>
        {{ 'forum.topics' | translate }}
      </a>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        @let topic = data()!;

        <header class="page-header">
          <div>
            <h1>{{ topic.title }}</h1>
            @if (topic.isLocked) {
              <p class="locked"><mat-icon aria-hidden="true">lock</mat-icon>{{ 'forum.lockedNote' | translate }}</p>
            }
          </div>
          <div class="header-actions">
            @if (topic.canModerate) {
              <button matButton="outlined" (click)="moderate({ pinned: !topic.isPinned })">
                <mat-icon>push_pin</mat-icon>
                {{ (topic.isPinned ? 'forum.unpin' : 'forum.pin') | translate }}
              </button>
              <button matButton="outlined" (click)="moderate({ locked: !topic.isLocked })">
                <mat-icon>{{ topic.isLocked ? 'lock_open' : 'lock' }}</mat-icon>
                {{ (topic.isLocked ? 'forum.unlock' : 'forum.lock') | translate }}
              </button>
            }
            @if (topic.canRemove) {
              <button matButton="outlined" (click)="removeTopic()">
                <mat-icon>delete</mat-icon>
                {{ 'common.remove' | translate }}
              </button>
            }
          </div>
        </header>

        <article class="panel post first">
          <p class="meta"><strong>{{ topic.authorName | localized }}</strong> · {{ topic.createdAt | appDate: true }}</p>
          <p class="body">{{ topic.body }}</p>
        </article>

        @for (post of topic.posts; track post.id) {
          <article class="panel post">
            <p class="meta">
              <strong>{{ post.authorName | localized }}</strong> · {{ post.createdAt | appDate: true }}
              @if (post.canRemove) {
                <button matIconButton [matTooltip]="'common.remove' | translate" [attr.aria-label]="'common.remove' | translate" (click)="removePost(post.id)">
                  <mat-icon>delete</mat-icon>
                </button>
              }
            </p>
            @if (post.isRemoved) {
              <p class="removed">{{ 'forum.postRemoved' | translate }}</p>
            } @else {
              <p class="body">{{ post.body }}</p>
            }
          </article>
        }

        @if (!topic.isLocked || topic.canModerate) {
          <form class="panel reply" (ngSubmit)="send()">
            <mat-form-field>
              <mat-label>{{ 'forum.yourReply' | translate }}</mat-label>
              <textarea matInput name="reply" rows="4" maxlength="8000" [(ngModel)]="reply"></textarea>
            </mat-form-field>
            <button type="submit" matButton="filled" [disabled]="busy() || !reply().trim()">{{ 'forum.post' | translate }}</button>
          </form>
        }
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .locked {
      display: flex;
      align-items: center;
      gap: var(--space-1);
    }
    h1,
    .body {
      overflow-wrap: anywhere;
    }
    .post {
      padding: var(--space-3) var(--space-4);
    }
    .post.first {
      border-inline-start: 3px solid var(--brand-primary);
    }
    .meta {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      min-height: 2.5rem;
      margin: 0;
      color: var(--mat-sys-on-surface-variant);

      button {
        margin-inline-start: auto;
      }
    }
    .body {
      margin: 0;
      white-space: pre-wrap;
    }
    .removed {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
      font-style: italic;
    }
    .reply {
      display: grid;
      gap: var(--space-2);
      justify-items: end;
      padding: var(--space-4);

      mat-form-field {
        width: 100%;
      }
    }
  `,
})
export class ForumTopic {
  private readonly community = inject(CommunityService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly cohortId = input.required<string>();
  readonly topicId = input.required<string>();

  protected readonly reply = signal('');
  protected readonly busy = signal(false);
  protected readonly resource = httpResource<TopicDetail>(() => `${FORUMS_URL}/${this.cohortId()}/topics/${this.topicId()}`);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected async send(): Promise<void> {
    await this.act(async () => {
      await this.community.reply(this.cohortId(), this.topicId(), this.reply().trim());
      this.reply.set('');
    });
  }

  protected moderate(change: { pinned?: boolean; locked?: boolean }): Promise<void> {
    return this.act(() => this.community.moderate(this.cohortId(), this.topicId(), change));
  }

  protected async removePost(postId: string): Promise<void> {
    if (await this.confirm('forum.removePostMessage')) {
      await this.act(() => this.community.removePost(this.cohortId(), this.topicId(), postId));
    }
  }

  protected async removeTopic(): Promise<void> {
    if (await this.confirm('forum.removeTopicMessage')) {
      await this.community.removeTopic(this.cohortId(), this.topicId());
      await this.router.navigate(['/forum', this.cohortId()]);
    }
  }

  private async confirm(messageKey: string): Promise<boolean> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'forum.removeTitle', messageKey, confirmKey: 'common.remove' },
    });

    return Boolean(await firstValueFrom(ref.afterClosed()));
  }

  private async act(work: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);

    try {
      await work();
      this.resource.reload();
    } catch {
      this.snackBar.open(this.translate.instant('community.sendFailed'), undefined, { duration: 4000 });
    } finally {
      this.busy.set(false);
    }
  }
}

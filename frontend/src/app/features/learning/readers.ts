import { Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/language.service';
import { BookChapter, LessonChoice, LessonPage } from './learning.models';

const SHARED = `
  :host {
    display: grid;
    gap: var(--space-3);
    width: 100%;
  }
  h3 {
    margin: 0;
    font: var(--mat-sys-title-medium);
  }
  .text {
    margin: 0;
    white-space: pre-wrap;
    overflow-wrap: anywhere;
  }
  .nav {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
  }
`;

@Component({
  selector: 'app-book-reader',
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (chapters().length > 1) {
      <nav [attr.aria-label]="'structure.contents' | translate" class="contents">
        @for (chapter of chapters(); track $index; let index = $index) {
          <button matButton [class.current]="index === at()" [attr.aria-current]="index === at() ? 'true' : null" (click)="at.set(index)">{{ index + 1 }}. {{ title(chapter) }}</button>
        }
      </nav>
    }
    <h3>{{ title(current()) }}</h3>
    <p class="text">{{ body(current()) }}</p>
    @if (chapters().length > 1) {
      <div class="nav">
        <button matButton="outlined" [disabled]="at() === 0" (click)="at.set(at() - 1)">{{ 'structure.previous' | translate }}</button>
        <button matButton="outlined" [disabled]="at() === chapters().length - 1" (click)="at.set(at() + 1)">{{ 'structure.next' | translate }}</button>
      </div>
    }
  `,
  styles:
    SHARED +
    `
    .contents {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }
    .current {
      background: var(--mat-sys-surface-container);
    }
  `,
})
export class BookReader {
  private readonly language = inject(LanguageService);

  readonly chapters = input.required<BookChapter[]>();

  protected readonly at = signal(0);
  protected readonly current = computed(() => this.chapters()[Math.min(this.at(), this.chapters().length - 1)]);

  protected title(chapter: BookChapter): string {
    return this.language.language() === 'ar' ? chapter.titleAr : chapter.titleEn;
  }

  protected body(chapter: BookChapter): string {
    return this.language.language() === 'ar' ? chapter.bodyAr : chapter.bodyEn;
  }
}

@Component({
  selector: 'app-lesson-player',
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    @if (isDone() && !reading()) {
      <p class="text">{{ 'structure.lessonDone' | translate }}</p>
      <div class="nav"><button matButton="outlined" (click)="reading.set(true)">{{ 'structure.restart' | translate }}</button></div>
    } @else {
      <h3>{{ arabic() ? current().titleAr : current().titleEn }}</h3>
      <p class="text">{{ arabic() ? current().bodyAr : current().bodyEn }}</p>
      <div class="nav">
        @for (choice of current().choices; track $index) {
          <button matButton="outlined" [disabled]="busy()" (click)="go(choice.target)">{{ arabic() ? choice.ar : choice.en }}</button>
        } @empty {
          @if (at() + 1 < pages().length) {
            <button matButton="outlined" (click)="go(at() + 1)">{{ 'structure.next' | translate }}</button>
          } @else {
            <button matButton="filled" [disabled]="busy()" (click)="go(null)">{{ 'structure.finish' | translate }}</button>
          }
        }
      </div>
    }
  `,
  styles: SHARED,
})
export class LessonPlayer {
  private readonly language = inject(LanguageService);

  readonly pages = input.required<LessonPage[]>();
  readonly isDone = input(false);
  readonly busy = input(false);
  readonly finished = output<void>();

  protected readonly at = signal(0);
  protected readonly reading = signal(false);
  protected readonly current = computed(() => this.pages()[this.at()]);
  protected readonly arabic = computed(() => this.language.language() === 'ar');

  protected go(target: LessonChoice['target']): void {
    if (target !== null) {
      this.at.set(target);
      return;
    }

    this.at.set(0);
    this.reading.set(false);

    if (!this.isDone()) {
      this.finished.emit();
    }
  }
}

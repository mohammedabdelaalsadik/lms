import { Component, computed, effect, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { BookChapter, LessonChoice, LessonPage } from './learning.models';

interface Draft extends BookChapter {
  key: number;
  choices: string;
}

/** "Arabic | English | page", where page counts from one and may be empty to end the lesson. */
function parseChoices(text: string, pages: number): LessonChoice[] | null {
  const rows = text
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);
  const parsed: LessonChoice[] = [];

  for (const row of rows) {
    const [ar, en, page] = row.split('|').map((part) => part.trim());
    const target = page ? Number(page) - 1 : null;

    if (!ar || !en || (target !== null && (!Number.isInteger(target) || target < 0 || target >= pages))) {
      return null;
    }

    parsed.push({ ar, en, target });
  }

  return parsed;
}

const describe = (choices: LessonChoice[]): string =>
  choices.map((c) => `${c.ar} | ${c.en} | ${c.target === null ? '' : c.target + 1}`).join('\n');

/** The chapters of a book or the pages of a lesson. */
@Component({
  selector: 'app-structure-field',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    @for (item of items(); track item.key; let index = $index) {
      <fieldset>
        <legend>{{ (lesson() ? 'structure.page' : 'structure.chapter') | translate }} {{ index + 1 }}</legend>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'structure.titleAr' | translate }}</mat-label>
          <input matInput dir="rtl" maxlength="200" [value]="item.titleAr" (input)="change(item, { titleAr: $any($event.target).value })" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'structure.titleEn' | translate }}</mat-label>
          <input matInput dir="ltr" maxlength="200" [value]="item.titleEn" (input)="change(item, { titleEn: $any($event.target).value })" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'structure.bodyAr' | translate }}</mat-label>
          <textarea matInput dir="rtl" rows="4" maxlength="20000" [value]="item.bodyAr" (input)="change(item, { bodyAr: $any($event.target).value })"></textarea>
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'structure.bodyEn' | translate }}</mat-label>
          <textarea matInput dir="ltr" rows="4" maxlength="20000" [value]="item.bodyEn" (input)="change(item, { bodyEn: $any($event.target).value })"></textarea>
        </mat-form-field>
        @if (lesson()) {
          <p class="hint">{{ 'structure.choices' | translate }}</p>
          <mat-form-field subscriptSizing="dynamic">
            <textarea matInput rows="2" [attr.aria-label]="'structure.choices' | translate" [value]="item.choices" (input)="change(item, { choices: $any($event.target).value })"></textarea>
          </mat-form-field>
        }
        <button matIconButton type="button" [attr.aria-label]="'common.remove' | translate" (click)="remove(item)"><mat-icon>delete</mat-icon></button>
      </fieldset>
    }

    <button matButton type="button" (click)="add()"><mat-icon>add</mat-icon>{{ (lesson() ? 'structure.addPage' : 'structure.addChapter') | translate }}</button>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-2);
    }
    fieldset {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: var(--space-3);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-s);
    }
    .hint {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
    fieldset button {
      justify-self: end;
    }
  `,
})
export class StructureField {
  private nextKey = 1;

  readonly lesson = input(false);
  readonly initial = input<(BookChapter | LessonPage)[] | null>(null);

  protected readonly items = signal<Draft[]>([this.blank()]);

  /** Null until everything is filled in. */
  readonly value = computed<LessonPage[] | null>(() => {
    const items = this.items();
    const pages: LessonPage[] = [];

    for (const item of items) {
      const choices = parseChoices(item.choices, items.length);
      const filled = [item.titleAr, item.titleEn, item.bodyAr, item.bodyEn].every((text) => text.trim());

      if (!filled || !choices) {
        return null;
      }

      const { titleAr, titleEn, bodyAr, bodyEn } = item;
      pages.push({ titleAr, titleEn, bodyAr, bodyEn, choices: this.lesson() ? choices : [] });
    }

    return pages.length ? pages : null;
  });

  constructor() {
    effect(() => {
      const initial = this.initial();

      if (initial?.length) {
        this.items.set(initial.map((page) => ({ ...page, key: this.nextKey++, choices: describe('choices' in page ? (page.choices ?? []) : []) })));
      }
    });
  }

  protected add(): void {
    this.items.update((list) => [...list, this.blank()]);
  }

  protected remove(item: Draft): void {
    this.items.update((list) => list.filter((i) => i.key !== item.key));
  }

  protected change(item: Draft, patch: Partial<Draft>): void {
    this.items.update((list) => list.map((i) => (i.key === item.key ? { ...i, ...patch } : i)));
  }

  private blank(): Draft {
    return { key: this.nextKey++, titleAr: '', titleEn: '', bodyAr: '', bodyEn: '', choices: '' };
  }
}

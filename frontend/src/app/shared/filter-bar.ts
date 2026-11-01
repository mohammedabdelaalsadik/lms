import { Component, OnDestroy, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** The same search, result count and reset on every list. Extra filters go in as content. */
@Component({
  selector: 'app-filter-bar',
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="bar" role="search">
      <div class="row">
        <label class="search">
          <mat-icon aria-hidden="true">search</mat-icon>
          <span class="visually-hidden">{{ label() }}</span>
          <input #box type="search" maxlength="100" [placeholder]="label()" [value]="search()" (input)="type(box.value)" />
          @if (search()) {
            <button type="button" class="clear-text" (click)="now('')" [attr.aria-label]="'filters.clearSearch' | translate">
              <mat-icon aria-hidden="true">close</mat-icon>
            </button>
          }
        </label>
        <ng-content select="[extra]" />
      </div>

      <div class="row foot">
        <ng-content />
        <span class="spacer"></span>
        @if (count() !== null) {
          <span class="count" role="status">{{ 'filters.count' | translate: { count: count() } }}</span>
        }
        @if (active() > 0) {
          <button matButton (click)="clear.emit()">
            <mat-icon>filter_alt_off</mat-icon>
            {{ 'filters.clear' | translate }}
          </button>
        }
      </div>
    </div>
  `,
  styles: `
    .bar {
      display: grid;
      gap: var(--space-3);
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-3);
    }
    .foot {
      min-height: 2.25rem;
    }
    .search {
      display: flex;
      flex: 1 1 16rem;
      align-items: center;
      gap: var(--space-2);
      min-height: 2.75rem;
      padding-inline: var(--space-3);
      background: var(--mat-sys-surface);
      border: 1px solid var(--line);
      border-radius: var(--radius-s);
      color: var(--mat-sys-on-surface-variant);
      transition:
        border-color 0.15s ease,
        box-shadow 0.15s ease;

      &:focus-within {
        border-color: var(--brand-primary);
        box-shadow: 0 0 0 3px color-mix(in srgb, var(--brand-primary) 18%, transparent);
      }
    }
    input {
      flex: 1;
      min-width: 0;
      padding: 0;
      border: 0;
      outline: 0;
      background: none;
      color: var(--mat-sys-on-surface);
      font: var(--mat-sys-body-large);

      &::-webkit-search-cancel-button {
        display: none;
      }
    }
    .clear-text {
      display: grid;
      place-items: center;
      padding: 2px;
      border: 0;
      border-radius: 50%;
      background: none;
      color: inherit;
      cursor: pointer;

      mat-icon {
        width: 18px;
        height: 18px;
        font-size: 18px;
      }
    }
    .spacer {
      flex: 1;
    }
    .count {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
      white-space: nowrap;
    }
  `,
})
export class FilterBar implements OnDestroy {
  private timer?: ReturnType<typeof setTimeout>;

  readonly label = input.required<string>();
  readonly search = input.required<string>();
  readonly count = input<number | null>(null);
  readonly active = input(0);
  readonly searchChange = output<string>();
  readonly clear = output<void>();

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }

  protected type(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.searchChange.emit(value.trim()), 300);
  }

  protected now(value: string): void {
    clearTimeout(this.timer);
    this.searchChange.emit(value);
  }
}

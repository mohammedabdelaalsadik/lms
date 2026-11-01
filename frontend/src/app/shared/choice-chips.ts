import { Component, computed, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

export interface Choice {
  value: string;
  labelKey: string;
}

/**
 * A one-of-many filter shown in full, so people see their options without opening a menu.
 * The empty choice means no filter, so it is not drawn: nothing selected is everything, and clicking a selected chip clears it.
 */
@Component({
  selector: 'app-choice-chips',
  imports: [TranslatePipe],
  template: `
    <div class="chips" role="group" [attr.aria-label]="label()">
      @for (choice of shown(); track choice.value) {
        <button
          type="button"
          class="chip"
          [class.on]="value() === choice.value"
          [attr.aria-pressed]="value() === choice.value"
          (click)="pick(choice.value)"
        >
          {{ choice.labelKey | translate }}
        </button>
      }
    </div>
  `,
  styles: `
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .chip {
      min-height: 2.25rem;
      padding: 0 var(--space-3);
      border: 1px solid var(--line);
      border-radius: 999px;
      background: var(--mat-sys-surface);
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-large);
      cursor: pointer;
      transition:
        background-color 0.15s ease,
        border-color 0.15s ease,
        color 0.15s ease;

      &:hover {
        border-color: var(--mat-sys-outline);
        color: var(--mat-sys-on-surface);
      }

      &.on {
        border-color: var(--brand-primary);
        background: var(--mat-sys-primary-container);
        color: var(--mat-sys-on-primary-container);
        font-weight: 600;
      }
    }
    @media (pointer: coarse) {
      .chip {
        min-height: 2.75rem;
      }
    }
  `,
})
export class ChoiceChips {
  readonly choices = input.required<Choice[]>();
  readonly value = input.required<string>();
  readonly label = input.required<string>();
  readonly valueChange = output<string>();

  protected readonly shown = computed(() => this.choices().filter((choice) => choice.value !== ''));
  private readonly canClear = computed(() => this.choices().some((choice) => choice.value === ''));

  protected pick(value: string): void {
    this.valueChange.emit(value === this.value() && this.canClear() ? '' : value);
  }
}

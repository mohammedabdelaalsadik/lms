import { ChangeDetectorRef, Component, DoCheck, computed, inject, input, signal } from '@angular/core';
import { ControlValueAccessor, FormControl, NgControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

const pad = (value: number) => String(value).padStart(2, '0');
const toKey = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;

function toDate(key: string | null | undefined): Date | null {
  if (!key) {
    return null;
  }

  const [year, month, day] = key.slice(0, 10).split('-').map(Number);
  return new Date(year, month - 1, day);
}

/**
 * A calendar date field, with an optional time, that reads and writes plain text such as
 * 2026-11-01 or 2026-11-01T14:30. The text box is read-only: choosing from the calendar
 * means a date can't be mistyped in either language.
 */
@Component({
  selector: 'app-date-field',
  imports: [MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <div class="wrap" [class.timed]="time()">
      <mat-form-field>
        <mat-label>{{ label() }}</mat-label>
        <input
          matInput
          readonly
          [matDatepicker]="picker"
          [min]="minDate()"
          [max]="maxDate()"
          [formControl]="picked"
          [required]="required"
          [errorStateMatcher]="matcher"
          (dateChange)="pick($event.value)"
          (click)="picker.open()"
          (blur)="touched()"
        />
        @if (clearable() && date()) {
          <button matIconSuffix matIconButton type="button" (click)="pick(null)" [attr.aria-label]="'common.clearDate' | translate">
            <mat-icon>close</mat-icon>
          </button>
        }
        <mat-datepicker-toggle matIconSuffix [for]="picker" [attr.aria-label]="'common.openCalendar' | translate" />
        <mat-datepicker #picker />
        <mat-error><ng-content /></mat-error>
      </mat-form-field>

      @if (time()) {
        <mat-form-field class="clock">
          <mat-label>{{ 'common.time' | translate }}</mat-label>
          <input matInput type="time" class="ltr-value" [value]="clock()" [disabled]="disabled() || !date()" (change)="setClock($any($event.target).value)" />
        </mat-form-field>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .wrap {
      display: grid;
      gap: var(--space-2);
    }
    .wrap > mat-form-field {
      width: 100%;
    }
    .timed {
      grid-template-columns: minmax(0, 1fr) 7.5rem;
    }
    input[readonly] {
      cursor: pointer;
    }
  `,
})
export class DateField implements ControlValueAccessor, DoCheck {
  private readonly control = inject(NgControl, { self: true, optional: true });
  private readonly changes = inject(ChangeDetectorRef);
  private showingError = false;
  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  readonly label = input.required<string>();
  readonly time = input(false);
  readonly clearable = input(false);
  readonly min = input<string | null>(null);
  readonly max = input<string | null>(null);

  /** Material updates a field's error state only when its input has a form control of its own. */
  protected readonly picked = new FormControl<Date | null>(null);
  protected readonly date = signal<Date | null>(null);
  protected readonly clock = signal('09:00');
  protected readonly disabled = signal(false);
  protected readonly minDate = computed(() => toDate(this.min()));
  protected readonly maxDate = computed(() => toDate(this.max()));

  protected readonly matcher: ErrorStateMatcher = {
    isErrorState: () => !!this.control?.invalid && !!this.control.touched,
  };

  protected get required(): boolean {
    return !!this.control?.control?.hasValidator(Validators.required);
  }

  constructor() {
    if (this.control) {
      this.control.valueAccessor = this;
    }
  }

  /** A form marked as touched sends no signal, so this view must notice the change itself. */
  ngDoCheck(): void {
    const showing = !!this.control?.invalid && !!this.control.touched;

    if (showing !== this.showingError) {
      this.showingError = showing;
      this.changes.markForCheck();
    }
  }

  writeValue(value: string | null): void {
    this.date.set(toDate(value));
    this.picked.setValue(this.date(), { emitEvent: false });

    if (value && value.length > 10) {
      this.clock.set(value.slice(11, 16));
    }
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);

    if (disabled) {
      this.picked.disable({ emitEvent: false });
    } else {
      this.picked.enable({ emitEvent: false });
    }
  }

  protected pick(value: Date | null): void {
    this.date.set(value);
    this.picked.setValue(value, { emitEvent: false });
    this.emit();
    this.onTouched();
  }

  protected setClock(value: string): void {
    this.clock.set(value || '00:00');
    this.emit();
  }

  protected touched(): void {
    this.onTouched();
  }

  private emit(): void {
    const date = this.date();

    if (!date) {
      this.onChange('');
    } else {
      this.onChange(this.time() ? `${toKey(date)}T${this.clock()}` : toKey(date));
    }
  }
}

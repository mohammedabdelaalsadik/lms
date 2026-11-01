import { Component, computed, inject, input } from '@angular/core';
import { LanguageService } from '../core/i18n/language.service';

const WORDS_AR = ['تعلّم', 'معرفة', 'مهارة', 'تدريب', 'شهادة', 'تطوّر'];
const WORDS_EN = ['Learn', 'Knowledge', 'Skills', 'Training', 'Certificate', 'Growth'];
const PLACES = [
  { top: 12, left: 52, size: 3.25, delay: 0 },
  { top: 22, left: 8, size: 2.5, delay: -2 },
  { top: 42, left: 42, size: 2.25, delay: -4 },
  { top: 54, left: 6, size: 3, delay: -1 },
  { top: 72, left: 50, size: 2.75, delay: -6 },
  { top: 84, left: 12, size: 2.25, delay: -3 },
];

/** Education words that drift slowly behind brand panels, in the reader's language. Decoration only, so screen readers skip it. */
@Component({
  selector: 'app-drift-letters',
  template: `
    @for (letter of letters(); track letter.text) {
      <span
        [style.inset-block-start.%]="letter.top"
        [style.inset-inline-start.%]="letter.left"
        [style.font-size]="'calc(' + letter.size + 'rem * var(--drift-size, 1))'"
        [style.animation-delay.s]="letter.delay"
        >{{ letter.text }}</span
      >
    }
  `,
  styles: `
    :host {
      position: absolute;
      inset: 0;
      overflow: hidden;
      pointer-events: none;
    }
    span {
      position: absolute;
      color: rgb(255 255 255 / 11%);
      white-space: nowrap;
      font-family: 'Readex Pro Variable', sans-serif;
      font-weight: 700;
      line-height: 1;
      animation: drift 9s ease-in-out infinite;
    }
    @keyframes drift {
      0%,
      100% {
        transform: translateY(0) rotate(-6deg);
      }
      50% {
        transform: translateY(-18px) rotate(6deg);
      }
    }
  `,
  host: { 'aria-hidden': 'true' },
})
export class DriftLetters {
  private readonly language = inject(LanguageService);

  /** Which spots to fill. A short banner has room for only a few words. */
  readonly spots = input([0, 1, 2, 3, 4, 5]);

  protected readonly letters = computed(() => {
    const arabic = this.language.language() === 'ar';
    return this.spots().map((index) => ({ ...PLACES[index], text: (arabic ? WORDS_AR : WORDS_EN)[index] }));
  });
}

/** A row of bars pulsing like a sound level. Decoration only. */
@Component({
  selector: 'app-sound-bars',
  template: `
    @for (bar of bars; track $index) {
      <span [style.height.%]="bar.height" [style.animation-delay.s]="bar.delay"></span>
    }
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: 6px;
      height: 3.5rem;
    }
    span {
      width: 6px;
      border-radius: 3px;
      background: linear-gradient(#ffd27a, #ff7a59);
      animation: pulse 1.4s ease-in-out infinite;
    }
    @keyframes pulse {
      0%,
      100% {
        transform: scaleY(0.4);
      }
      50% {
        transform: scaleY(1);
      }
    }
  `,
  host: { 'aria-hidden': 'true' },
})
export class SoundBars {
  protected readonly bars = [40, 62, 48, 78, 54, 96, 70, 100, 58, 84, 46, 66, 38, 72, 52, 88].map((height, index) => ({
    height,
    delay: -(index * 0.11),
  }));
}

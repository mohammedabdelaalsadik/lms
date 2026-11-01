import { Component, computed, input, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

export interface ChartPoint {
  label: string;
  value: number;
}

const WIDTH = 720;
const HEIGHT = 220;
const PAD = { top: 16, right: 16, bottom: 28, left: 36 };

/** The nearest round axis top above the largest value, so ticks read as clean numbers. */
function niceTop(max: number): number {
  if (max <= 4) {
    return 4;
  }

  const step = 10 ** Math.floor(Math.log10(max));
  const top = [1, 2, 2.5, 5, 10].map((m) => m * step).find((m) => m >= max) ?? max;
  return Number.isInteger(top / 4) ? top : Math.ceil(top / 4) * 4;
}

/** One measure over time. Hover or keyboard shows each month, and a table holds every value. */
@Component({
  selector: 'app-area-chart',
  imports: [TranslatePipe],
  template: `
    <div class="plot" dir="ltr">
      <svg
        [attr.viewBox]="'0 0 ' + width + ' ' + height"
        role="img"
        [attr.aria-label]="label()"
        tabindex="0"
        (pointermove)="track($event)"
        (pointerleave)="active.set(null)"
        (focus)="active.set(points().length - 1)"
        (blur)="active.set(null)"
        (keydown)="key($event)"
      >
        @for (tick of ticks(); track tick.value) {
          <line class="grid" [attr.x1]="left" [attr.x2]="right" [attr.y1]="tick.y" [attr.y2]="tick.y" />
          <text class="axis" [attr.x]="left - 8" [attr.y]="tick.y + 4" text-anchor="end">{{ tick.value }}</text>
        }
        <path class="area" [attr.d]="area()" />
        <path class="line" [attr.d]="line()" />
        @for (point of coords(); track $index) {
          @if ($index % 2 === 1 || points().length <= 6) {
            <text class="axis" [attr.x]="point.x" [attr.y]="height - 8" text-anchor="middle">{{ points()[$index].label }}</text>
          }
        }
        @if (focus(); as spot) {
          <line class="cross" [attr.x1]="spot.x" [attr.x2]="spot.x" [attr.y1]="top" [attr.y2]="bottom" />
          <circle class="dot" [attr.cx]="spot.x" [attr.cy]="spot.y" r="5" />
        } @else if (last(); as end) {
          <circle class="dot" [attr.cx]="end.x" [attr.cy]="end.y" r="4" />
        }
      </svg>
      @if (focus(); as spot) {
        <div class="tip" [style.left.%]="(spot.x / width) * 100" [style.top.%]="(spot.y / height) * 100">
          <small>{{ spot.label }}</small>
          <strong>{{ spot.value }}</strong>
        </div>
      }
    </div>
    <details>
      <summary>{{ 'insights.asTable' | translate }}</summary>
      <table>
        <tbody>
          @for (point of points(); track $index) {
            <tr>
              <th scope="row">{{ point.label }}</th>
              <td>{{ point.value }}</td>
            </tr>
          }
        </tbody>
      </table>
    </details>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-2);
    }
    .plot {
      position: relative;
    }
    svg {
      display: block;
      width: 100%;
      height: auto;
      touch-action: pan-y;
    }
    svg:focus-visible {
      outline-offset: 4px;
    }
    .grid {
      stroke: var(--line);
      stroke-width: 1;
      vector-effect: non-scaling-stroke;
    }
    .axis {
      fill: var(--mat-sys-on-surface-variant);
      font-size: 12px;
    }
    @media (max-width: 600px) {
      .axis {
        font-size: 22px;
      }
    }
    .line {
      fill: none;
      stroke: var(--brand-primary);
      stroke-width: 2;
      stroke-linejoin: round;
      stroke-linecap: round;
      vector-effect: non-scaling-stroke;
    }
    .area {
      fill: var(--brand-primary);
      opacity: 0.1;
    }
    .cross {
      stroke: var(--mat-sys-outline);
      stroke-width: 1;
      vector-effect: non-scaling-stroke;
    }
    .dot {
      fill: var(--brand-primary);
      stroke: var(--mat-sys-surface);
      stroke-width: 2;
    }
    .tip {
      position: absolute;
      display: grid;
      gap: 2px;
      padding: var(--space-1) var(--space-2);
      border: 1px solid var(--line);
      border-radius: var(--radius-s);
      background: var(--mat-sys-surface);
      box-shadow: var(--shadow-card);
      white-space: nowrap;
      transform: translate(-50%, calc(-100% - 12px));
      pointer-events: none;
    }
    .tip small,
    summary {
      color: var(--mat-sys-on-surface-variant);
    }
    summary {
      cursor: pointer;
      font: var(--mat-sys-label-medium);
    }
    table {
      width: 100%;
      margin-block-start: var(--space-2);
      border-collapse: collapse;
    }
    th,
    td {
      padding: 2px var(--space-2);
      border-block-end: 1px solid var(--line);
      font: var(--mat-sys-body-small);
      text-align: start;
    }
    td {
      text-align: end;
    }
  `,
})
export class AreaChart {
  readonly points = input.required<ChartPoint[]>();
  readonly label = input.required<string>();

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly left = PAD.left;
  protected readonly right = WIDTH - PAD.right;
  protected readonly top = PAD.top;
  protected readonly bottom = HEIGHT - PAD.bottom;
  protected readonly active = signal<number | null>(null);

  private readonly maximum = computed(() => niceTop(Math.max(...this.points().map((p) => p.value), 1)));

  protected readonly coords = computed(() => {
    const count = this.points().length;
    return this.points().map((point, index) => ({
      x: count < 2 ? this.left : this.left + (index / (count - 1)) * (this.right - this.left),
      y: this.bottom - (point.value / this.maximum()) * (this.bottom - this.top),
      ...point,
    }));
  });

  protected readonly ticks = computed(() =>
    [0, 1, 2, 3, 4].map((step) => ({
      value: (this.maximum() / 4) * step,
      y: this.bottom - (step / 4) * (this.bottom - this.top),
    })),
  );

  protected readonly line = computed(() => this.coords().map((p, i) => `${i ? 'L' : 'M'}${p.x} ${p.y}`).join(' '));
  protected readonly area = computed(() => `${this.line()} L${this.right} ${this.bottom} L${this.left} ${this.bottom} Z`);
  protected readonly focus = computed(() => {
    const index = this.active();
    return index === null ? null : this.coords()[index];
  });
  protected readonly last = computed(() => this.coords().at(-1) ?? null);

  protected track(event: PointerEvent): void {
    const box = (event.currentTarget as SVGElement).getBoundingClientRect();
    const x = ((event.clientX - box.left) / box.width) * WIDTH;
    const coords = this.coords();
    this.active.set(coords.reduce((best, point, index) => (Math.abs(point.x - x) < Math.abs(coords[best].x - x) ? index : best), 0));
  }

  protected key(event: KeyboardEvent): void {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;

    if (step) {
      event.preventDefault();
      this.active.update((index) => Math.min(this.points().length - 1, Math.max(0, (index ?? 0) + step)));
    }
  }
}

/** Parts ranked largest first, in one colour. Every value is printed, so nothing depends on hovering. */
@Component({
  selector: 'app-ranked-bars',
  template: `
    @for (item of points(); track $index) {
      <div class="row">
        <span class="name" [attr.title]="item.label">{{ item.label }}</span>
        <span class="track"><span class="fill" [style.inline-size.%]="(item.value / maximum()) * 100"></span></span>
        <span class="value">{{ item.value }}{{ unit() }}</span>
      </div>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-3);
    }
    .row {
      display: grid;
      grid-template-columns: minmax(0, 11rem) minmax(0, 1fr) 3rem;
      align-items: center;
      gap: var(--space-3);
    }
    .name {
      overflow: hidden;
      font: var(--mat-sys-body-small);
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .track {
      display: block;
    }
    .fill {
      display: block;
      min-inline-size: 4px;
      height: 0.75rem;
      border-start-end-radius: 4px;
      border-end-end-radius: 4px;
      background: var(--brand-primary);
      transition: filter 0.15s ease;
    }
    .row:hover .fill {
      filter: brightness(1.15);
    }
    .value {
      font: var(--mat-sys-label-large);
      text-align: end;
    }
    @media (max-width: 480px) {
      .row {
        grid-template-columns: minmax(0, 7rem) minmax(0, 1fr) 2.5rem;
      }
    }
  `,
})
export class RankedBars {
  readonly points = input.required<ChartPoint[]>();
  readonly unit = input('');
  readonly scale = input<number | null>(null);

  protected readonly maximum = computed(() => this.scale() ?? Math.max(...this.points().map((p) => p.value), 1));
}

export interface Segment extends ChartPoint {
  tone: 'one' | 'two' | 'three';
}

/** One whole split into a few parts, each labelled in place, with a legend beneath. */
@Component({
  selector: 'app-segment-bar',
  template: `
    <div class="bar" role="img" [attr.aria-label]="summary()">
      @for (part of shown(); track part.label) {
        <span class="part" [class]="part.tone" [style.flex-grow]="part.value" [attr.title]="part.label + ': ' + part.value">
          @if (share(part) >= 14) {
            {{ part.value }}
          }
        </span>
      }
    </div>
    <ul>
      @for (part of parts(); track part.label) {
        <li>
          <span class="swatch" [class]="part.tone" aria-hidden="true"></span>
          {{ part.label }}
          <strong>{{ part.value }}</strong>
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-3);
    }
    .bar {
      display: flex;
      gap: 2px;
      height: 3.5rem;
    }
    .part {
      display: grid;
      flex-basis: 0;
      min-width: 6px;
      place-items: center;
      border-radius: 4px;
      color: #ffffff;
      font: var(--mat-sys-title-medium);
      font-weight: 700;
    }
    .one {
      background: #007c98;
    }
    .two {
      background: #b5601a;
    }
    .three {
      background: #6b5bbd;
    }
    ul {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-4);
      margin: 0;
      padding: 0;
      list-style: none;
      font: var(--mat-sys-body-small);
    }
    li {
      display: flex;
      align-items: center;
      gap: var(--space-1);
    }
    .swatch {
      width: 0.625rem;
      height: 0.625rem;
      border-radius: 3px;
    }
  `,
})
export class SegmentBar {
  readonly parts = input.required<Segment[]>();

  protected readonly shown = computed(() => this.parts().filter((p) => p.value > 0));
  protected readonly summary = computed(() => this.parts().map((p) => `${p.label}: ${p.value}`).join('، '));

  protected share(part: Segment): number {
    const total = this.parts().reduce((sum, p) => sum + p.value, 0);
    return total ? (part.value / total) * 100 : 0;
  }
}

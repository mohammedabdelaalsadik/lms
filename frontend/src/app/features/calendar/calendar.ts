import { LIVE_CLASSES_ENABLED } from '../../core/features';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CalendarEvent, CalendarService, localDay } from './calendar.service';

interface Day {
  key: string;
  date: Date;
  inMonth: boolean;
  isToday: boolean;
  hijri: string;
  events: CalendarEvent[];
  label: string;
}

const CELLS = 42;
const HIJRI_LOCALE = 'en-u-ca-islamic-umalqura-nu-latn';
export const KIND_ICONS = { start: 'flag', end: 'sports_score', quiz: 'quiz', live: 'videocam', assignment: 'assignment' };

@Component({
  selector: 'app-calendar',
  imports: [LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  templateUrl: './calendar.html',
  styleUrl: './calendar.scss',
})
export class CalendarPage implements OnInit {
  protected readonly liveClasses = LIVE_CLASSES_ENABLED;
  private readonly calendar = inject(CalendarService);
  private readonly languages = inject(LanguageService);
  private readonly translate = inject(TranslateService);

  protected readonly icons = KIND_ICONS;
  protected readonly events = signal<CalendarEvent[] | null>(null);
  protected readonly failed = signal(false);
  protected readonly month = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  protected readonly selectedKey = signal(localDay(new Date()));

  private readonly locale = computed(() => (this.languages.language() === 'ar' ? 'ar-SA-u-ca-gregory-nu-latn' : 'en-GB'));

  protected readonly title = computed(() =>
    new Intl.DateTimeFormat(this.locale(), { month: 'long', year: 'numeric' }).format(this.month()),
  );

  protected readonly hijriTitle = computed(() => {
    const start = this.month();
    const end = new Date(start.getFullYear(), start.getMonth() + 1, 0);
    const locale = this.languages.language() === 'ar' ? 'ar-SA-u-ca-islamic-umalqura-nu-latn' : 'en-u-ca-islamic-umalqura-nu-latn';
    const format = new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric' });
    const [first, last] = [format.format(start), format.format(end)];
    return first === last ? first : `${first} – ${last}`;
  });

  protected readonly weekdays = computed(() => {
    const format = new Intl.DateTimeFormat(this.locale(), { weekday: 'short' });
    // 1 November 2026 is a Sunday, the first day of the week in Saudi Arabia.
    return Array.from({ length: 7 }, (_, i) => format.format(new Date(2026, 10, 1 + i)));
  });

  protected readonly days = computed<Day[]>(() => {
    const first = this.month();
    const today = localDay(new Date());
    const hijri = new Intl.DateTimeFormat(HIJRI_LOCALE, { day: 'numeric' });
    const label = new Intl.DateTimeFormat(this.locale(), { dateStyle: 'full' });
    const byDay = new Map<string, CalendarEvent[]>();

    for (const event of this.events() ?? []) {
      byDay.set(event.date, [...(byDay.get(event.date) ?? []), event]);
    }

    return Array.from({ length: CELLS }, (_, i) => {
      const date = new Date(first.getFullYear(), first.getMonth(), 1 - first.getDay() + i);
      const key = localDay(date);
      const events = byDay.get(key) ?? [];

      return {
        key,
        date,
        inMonth: date.getMonth() === first.getMonth(),
        isToday: key === today,
        hijri: hijri.format(date).replace(/\D/g, ''),
        events,
        label: `${label.format(date)}${events.length ? ` - ${this.translate.instant('calendar.eventCount', { count: events.length })}` : ''}`,
      };
    });
  });

  protected readonly selected = computed(() => this.days().find((d) => d.key === this.selectedKey()) ?? null);

  protected readonly selectedLabel = computed(() => {
    const [year, month, day] = this.selectedKey().split('-').map(Number);
    return new Intl.DateTimeFormat(this.locale(), { dateStyle: 'full' }).format(new Date(year, month - 1, day));
  });

  protected readonly selectedEvents = computed(() =>
    (this.events() ?? []).filter((e) => e.date === this.selectedKey()),
  );

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.failed.set(false);

    try {
      this.events.set(await this.calendar.load());
    } catch {
      this.failed.set(true);
    }
  }

  protected move(step: number): void {
    const current = this.month();
    this.month.set(new Date(current.getFullYear(), current.getMonth() + step, 1));
  }

  protected goToday(): void {
    const now = new Date();
    this.month.set(new Date(now.getFullYear(), now.getMonth(), 1));
    this.selectedKey.set(localDay(now));
  }
}

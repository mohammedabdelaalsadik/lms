import { httpResource } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { DriftLetters, SoundBars } from '../../shared/brand-motion';
import { Course, LocalizedText, Paged, UserSummary } from '../../core/models';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { KIND_ICONS } from '../calendar/calendar';
import { Insights } from './insights';
import { CalendarEvent, CalendarService, localDay } from '../calendar/calendar.service';
import { StatePanel } from '../../shared/state-panel';

const COURSES = '/api/v1/courses';

interface TimelineItem {
  kind: 'Quiz' | 'Assignment' | 'WorkshopSubmission' | 'WorkshopReview' | 'LiveClass';
  title: LocalizedText;
  cohort: LocalizedText;
  dueAt: string | null;
  isOverdue: boolean;
  link: string;
}

@Component({
  selector: 'app-dashboard',
  imports: [Insights, DriftLetters, SoundBars, AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard implements OnInit {
  private readonly calendar = inject(CalendarService);
  private readonly languages = inject(LanguageService);

  protected readonly auth = inject(AuthService);
  protected readonly icons = KIND_ICONS;
  protected readonly upcoming = signal<CalendarEvent[] | null>(null);
  protected readonly timelineIcons: Record<TimelineItem['kind'], string> = {
    Quiz: 'quiz',
    Assignment: 'assignment',
    WorkshopSubmission: 'edit_note',
    WorkshopReview: 'rate_review',
    LiveClass: 'videocam',
  };
  private readonly timelineResource = httpResource<TimelineItem[]>(() => (this.auth.hasRole('Trainee') ? '/api/v1/assessment/my/timeline' : undefined));
  protected readonly timeline = computed(() => (this.timelineResource.hasValue() ? this.timelineResource.value().slice(0, 6) : []));

  protected readonly today = computed(() => {
    const arabic = this.languages.language() === 'ar';
    const format = (locale: string) => new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(new Date());
    return arabic ? format('ar-SA-u-ca-islamic-umalqura-nu-latn') : format('en-GB');
  });

  protected readonly showInsights = computed(() => this.auth.hasRole('Admin', 'Coordinator', 'Instructor', 'Auditor'));
  private readonly isManager = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  private readonly canSeeUsers = computed(() => this.auth.hasRole('Admin', 'Auditor'));

  protected readonly published = httpResource<Paged<Course>>(() => ({
    url: COURSES,
    params: { status: 'Published', pageSize: 5 },
  }));

  private readonly drafts = httpResource<Paged<Course>>(() =>
    this.isManager() ? { url: COURSES, params: { status: 'Draft', pageSize: 1 } } : undefined,
  );

  private readonly myLearning = httpResource<unknown[]>(() => (this.auth.hasRole('Trainee') ? '/api/v1/learning/my' : undefined));
  private readonly myCertificates = httpResource<{ status: string }[]>(() => (this.auth.hasRole('Trainee') ? '/api/v1/certificates/my' : undefined));

  private readonly users = httpResource<Paged<UserSummary>>(() =>
    this.canSeeUsers() ? { url: '/api/v1/users', params: { pageSize: 1 } } : undefined,
  );

  protected readonly recent = computed(() =>
    this.published.hasValue() ? this.published.value().items : null,
  );

  protected readonly stats = computed(() => {
    const items = [
      { key: 'dashboard.publishedCourses', icon: 'menu_book', value: this.total(this.published) },
    ];

    if (this.auth.hasRole('Trainee')) {
      const learning = this.myLearning.hasValue() ? (this.myLearning.value()?.length ?? null) : null;
      const certificates = this.myCertificates.hasValue() ? (this.myCertificates.value()?.filter((c) => c.status === 'Issued').length ?? null) : null;
      items.push({ key: 'dashboard.myCourses', icon: 'school', value: learning });
      items.push({ key: 'dashboard.myCertificates', icon: 'workspace_premium', value: certificates });
    }

    if (this.isManager()) {
      items.push({ key: 'dashboard.draftCourses', icon: 'edit_note', value: this.total(this.drafts) });
    }

    if (this.canSeeUsers()) {
      items.push({ key: 'dashboard.users', icon: 'group', value: this.total(this.users) });
    }

    return items;
  });

  ngOnInit(): void {
    void this.loadUpcoming();
  }

  private async loadUpcoming(): Promise<void> {
    const today = localDay(new Date());

    try {
      const events = await this.calendar.load();
      this.upcoming.set(events.filter((e) => e.date >= today).sort((a, b) => a.date.localeCompare(b.date)).slice(0, 5));
    } catch {
      this.upcoming.set([]);
    }
  }

  private total(resource: { hasValue(): boolean; value(): Paged<unknown> | undefined }): number | null {
    return resource.hasValue() ? (resource.value()?.total ?? null) : null;
  }
}

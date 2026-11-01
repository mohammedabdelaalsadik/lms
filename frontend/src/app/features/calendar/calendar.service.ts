import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Cohort, LocalizedText, Paged } from '../../core/models';
import { MyAssessments } from '../assessment/assessment.models';
import { myUrl } from '../assessment/assessment.service';
import { MyLearningItem } from '../learning/learning.models';
import { LEARNING_URL } from '../learning/learning.service';

export type CalendarKind = 'start' | 'end' | 'quiz' | 'assignment' | 'live';

export interface CalendarEvent {
  date: string;
  kind: CalendarKind;
  title: LocalizedText;
  link: string[];
}

const pad = (value: number) => String(value).padStart(2, '0');

/** The day an event falls on where the person is, not in UTC. */
export function localDay(value: string | Date): string {
  const date = new Date(value);
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

interface LiveClassDate {
  title: LocalizedText;
  startsAt: string;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class CalendarService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  /** Trainees see their own dates, instructors the cohorts they teach, and other staff all cohorts. */
  load(): Promise<CalendarEvent[]> {
    return this.auth.hasRole('Trainee') ? this.forTrainee() : this.forStaff();
  }

  private async forStaff(): Promise<CalendarEvent[]> {
    const page = await firstValueFrom(this.http.get<Paged<Cohort>>('/api/v1/cohorts', { params: { pageSize: 100 } }));
    const mine = this.auth.hasRole('Instructor') ? page.items.filter((c) => c.instructorId === this.auth.user()?.id) : page.items;

    const today = localDay(new Date());
    const live = await this.liveClasses(mine.filter((c) => c.endsOn >= today).map((c) => c.id));

    return [
      ...mine.flatMap((c) => [
        { date: c.startsOn, kind: 'start' as const, title: c.courseTitle, link: ['/cohorts', c.id] },
        { date: c.endsOn, kind: 'end' as const, title: c.courseTitle, link: ['/cohorts', c.id] },
      ]),
      ...live,
    ];
  }

  /** A cohort whose classes cannot be read simply shows none. */
  private async liveClasses(cohortIds: string[]): Promise<CalendarEvent[]> {
    const lists = await Promise.allSettled(
      cohortIds.map(async (id) => ({
        id,
        classes: (await firstValueFrom(this.http.get<{ classes: LiveClassDate[] }>(`${LEARNING_URL}/cohorts/${id}/live`))).classes,
      })),
    );

    return lists.flatMap((list) =>
      list.status === 'fulfilled'
        ? list.value.classes
            .filter((c) => c.status !== 'Ended')
            .map((c) => ({ date: localDay(c.startsAt), kind: 'live' as const, title: c.title, link: ['/live', list.value.id] }))
        : [],
    );
  }

  private async forTrainee(): Promise<CalendarEvent[]> {
    const items = await firstValueFrom(this.http.get<MyLearningItem[]>(`${LEARNING_URL}/my`));

    const assessed = await Promise.allSettled(
      items.filter((i) => i.hasStarted).map(async (item) => ({
        item,
        data: await firstValueFrom(this.http.get<MyAssessments>(myUrl(item.cohortId))),
      })),
    );

    const events: CalendarEvent[] = items.flatMap((i) => [
      { date: i.startsOn, kind: 'start' as const, title: i.courseTitle, link: ['/learn'] },
      { date: i.endsOn, kind: 'end' as const, title: i.courseTitle, link: ['/learn'] },
    ]);

    for (const result of assessed) {
      if (result.status !== 'fulfilled') {
        continue;
      }

      const { item, data } = result.value;
      const link = ['/learn', item.cohortId, 'assessments'];

      for (const quiz of data.quizzes) {
        if (quiz.closesAt) {
          events.push({ date: localDay(quiz.closesAt), kind: 'quiz', title: quiz.title, link });
        }
      }

      for (const assignment of data.assignments) {
        events.push({ date: localDay(assignment.dueAt), kind: 'assignment', title: assignment.title, link });
      }
    }

    return [...events, ...(await this.liveClasses(items.map((i) => i.cohortId)))];
  }
}

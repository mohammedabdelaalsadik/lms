import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ActivityInput, AttendanceEntry, CourseContent, MoveDirection } from './learning.models';

export const LEARNING_URL = '/api/v1/learning';

export const contentUrl = (courseId: string) => `${LEARNING_URL}/courses/${courseId}/content`;

@Injectable({ providedIn: 'root' })
export class LearningService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  saveSettings(courseId: string, settings: Pick<CourseContent, 'isSequential' | 'minAttendancePercent' | 'plannedSessions' | 'passMarkPercent'>) {
    return this.http.put<CourseContent>(`${contentUrl(courseId)}/settings`, settings);
  }

  addSection(courseId: string, title: { titleAr: string; titleEn: string }) {
    return this.http.post<CourseContent>(`${contentUrl(courseId)}/sections`, title);
  }

  renameSection(courseId: string, sectionId: string, title: { titleAr: string; titleEn: string }) {
    return this.http.put<CourseContent>(`${contentUrl(courseId)}/sections/${sectionId}`, title);
  }

  removeSection(courseId: string, sectionId: string) {
    return this.http.delete<CourseContent>(`${contentUrl(courseId)}/sections/${sectionId}`);
  }

  moveSection(courseId: string, sectionId: string, direction: MoveDirection) {
    return this.http.post<CourseContent>(`${contentUrl(courseId)}/sections/${sectionId}/move`, { direction });
  }

  addActivity(courseId: string, sectionId: string, input: ActivityInput) {
    return this.http.post<CourseContent>(`${contentUrl(courseId)}/sections/${sectionId}/activities`, input);
  }

  addScorm(courseId: string, sectionId: string, input: ActivityInput, file: File) {
    const form = new FormData();
    form.set('titleAr', input.titleAr);
    form.set('titleEn', input.titleEn);
    form.set('isRequired', String(input.isRequired));
    form.set('file', file);
    return this.http.post<CourseContent>(`${contentUrl(courseId)}/sections/${sectionId}/scorm`, form);
  }

  launchScorm(cohortId: string, activityId: string) {
    return firstValueFrom(this.http.post<{ url: string }>(`${LEARNING_URL}/cohorts/${cohortId}/activities/${activityId}/scorm`, {}));
  }

  addFile(courseId: string, sectionId: string, input: ActivityInput, file: File) {
    const form = new FormData();
    form.set('titleAr', input.titleAr);
    form.set('titleEn', input.titleEn);
    form.set('isRequired', String(input.isRequired));
    form.set('file', file);
    return this.http.post<CourseContent>(`${contentUrl(courseId)}/sections/${sectionId}/files`, form);
  }

  updateActivity(courseId: string, activityId: string, input: ActivityInput) {
    return this.http.put<CourseContent>(`${contentUrl(courseId)}/activities/${activityId}`, input);
  }

  removeActivity(courseId: string, activityId: string) {
    return this.http.delete<CourseContent>(`${contentUrl(courseId)}/activities/${activityId}`);
  }

  moveActivity(courseId: string, activityId: string, direction: MoveDirection) {
    return this.http.post<CourseContent>(`${contentUrl(courseId)}/activities/${activityId}/move`, { direction });
  }

  complete(cohortId: string, activityId: string) {
    return this.http.post<void>(`${LEARNING_URL}/cohorts/${cohortId}/activities/${activityId}/complete`, {});
  }

  saveAttendance(cohortId: string, date: string, entries: AttendanceEntry[]) {
    return this.http.put<void>(`${LEARNING_URL}/cohorts/${cohortId}/attendance`, { date, entries });
  }

  /** Goes through HttpClient so an expired session renews before the download. */
  async download(activityId: string, fileName: string, cohortId?: string): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`${LEARNING_URL}/activities/${activityId}/file`, {
        responseType: 'blob',
        params: cohortId ? { cohortId } : {},
      }),
    );

    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }
}

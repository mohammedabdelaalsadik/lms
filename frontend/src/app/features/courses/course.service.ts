import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Course } from '../../core/models';

export interface CourseInput {
  code?: string;
  titleAr: string;
  titleEn: string;
  descriptionAr: string;
  descriptionEn: string;
  durationHours: number;
}

const BASE = '/api/v1/courses';

@Injectable({ providedIn: 'root' })
export class CourseService {
  private readonly http = inject(HttpClient);

  readonly baseUrl = BASE;

  create(input: CourseInput) {
    return this.http.post<Course>(BASE, input);
  }

  update(id: string, input: CourseInput) {
    return this.http.put<Course>(`${BASE}/${id}`, input);
  }

  move(id: string, categoryId: string | null) {
    return this.http.put<void>(`${BASE}/${id}/category`, { categoryId });
  }

  publish(id: string) {
    return this.http.post<void>(`${BASE}/${id}/publish`, {});
  }

  archive(id: string) {
    return this.http.post<void>(`${BASE}/${id}/archive`, {});
  }
}

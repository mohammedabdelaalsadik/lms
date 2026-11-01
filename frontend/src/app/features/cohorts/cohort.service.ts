import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Cohort, EnrollmentStatus } from '../../core/models';

export interface CohortInput {
  courseId: string;
  nameAr: string;
  nameEn: string;
  branch: string;
  instructorId: string;
  startsOn: string;
  endsOn: string;
  capacity: number;
  requiresApproval: boolean;
}

export const COHORTS_URL = '/api/v1/cohorts';

@Injectable({ providedIn: 'root' })
export class CohortService {
  private readonly http = inject(HttpClient);

  create(input: CohortInput) {
    return this.http.post<string>(COHORTS_URL, input);
  }

  close(id: string) {
    return this.http.post<void>(`${COHORTS_URL}/${id}/close`, {});
  }

  enroll(id: string) {
    return this.http.post<EnrollmentStatus>(`${COHORTS_URL}/${id}/enroll`, {});
  }

  withdraw(id: string) {
    return this.http.post<void>(`${COHORTS_URL}/${id}/withdraw`, {});
  }

  approve(cohort: Cohort, enrollmentId: string) {
    return this.http.post<void>(`${COHORTS_URL}/${cohort.id}/enrollments/${enrollmentId}/approve`, {});
  }

  reject(cohort: Cohort, enrollmentId: string, reason: string) {
    return this.http.post<void>(`${COHORTS_URL}/${cohort.id}/enrollments/${enrollmentId}/reject`, {
      reason,
    });
  }
}

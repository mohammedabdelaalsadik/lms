import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Assignment, Attempt, Quiz, QuizInput } from './assessment.models';

export const staffUrl = (cohortId: string) => `/api/v1/assessment/cohorts/${cohortId}`;
export const myUrl = (cohortId: string) => `/api/v1/assessment/my/cohorts/${cohortId}`;

export interface AssignmentInput {
  titleAr: string;
  titleEn: string;
  instructionsAr: string;
  instructionsEn: string;
  dueAt: string;
  weight: number;
}

@Injectable({ providedIn: 'root' })
export class AssessmentService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  saveQuiz(cohortId: string, quizId: string | null, input: QuizInput) {
    const base = `${staffUrl(cohortId)}/quizzes`;
    return quizId ? this.http.put<Quiz>(`${base}/${quizId}`, input) : this.http.post<Quiz>(base, input);
  }

  publishQuiz(cohortId: string, quizId: string) {
    return this.http.post<void>(`${staffUrl(cohortId)}/quizzes/${quizId}/publish`, {});
  }

  createAssignment(cohortId: string, input: AssignmentInput) {
    return this.http.post<Assignment>(`${staffUrl(cohortId)}/assignments`, input);
  }

  review(cohortId: string, submissionId: string, percent: number, feedback: string) {
    return this.http.put<void>(`${staffUrl(cohortId)}/submissions/${submissionId}/review`, { percent, feedback });
  }

  setGrade(cohortId: string, itemId: string, traineeId: string, percent: number) {
    return this.http.put<void>(`${staffUrl(cohortId)}/gradebook/items/${itemId}/grades/${traineeId}`, { percent });
  }

  publishGrades(cohortId: string, itemId: string) {
    return this.http.post<void>(`${staffUrl(cohortId)}/gradebook/items/${itemId}/publish`, {});
  }

  requestCorrection(cohortId: string, itemId: string, traineeId: string, newPercent: number, reason: string) {
    return this.http.post<void>(`${staffUrl(cohortId)}/gradebook/items/${itemId}/grades/${traineeId}/corrections`, { newPercent, reason });
  }

  decideCorrection(cohortId: string, correctionId: string, approve: boolean) {
    return this.http.post<void>(`${staffUrl(cohortId)}/gradebook/corrections/${correctionId}/${approve ? 'approve' : 'reject'}`, {});
  }

  startAttempt(cohortId: string, quizId: string) {
    return this.http.post<Attempt>(`${myUrl(cohortId)}/quizzes/${quizId}/attempts`, {});
  }

  saveAnswer(cohortId: string, attemptId: string, questionId: string, response: string) {
    return this.http.put<void>(`${myUrl(cohortId)}/attempts/${attemptId}/answers`, { questionId, response });
  }

  submitAttempt(cohortId: string, attemptId: string) {
    return this.http.post<void>(`${myUrl(cohortId)}/attempts/${attemptId}/submit`, {});
  }

  submitAssignment(cohortId: string, assignmentId: string, file: File) {
    const form = new FormData();
    form.set('file', file);
    return this.http.post<void>(`${myUrl(cohortId)}/assignments/${assignmentId}/submission`, form);
  }

  /** Goes through HttpClient so an expired session renews before the download. */
  async downloadSubmission(cohortId: string, submissionId: string, fileName: string): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`${staffUrl(cohortId)}/submissions/${submissionId}/file`, { responseType: 'blob' }),
    );
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }
}

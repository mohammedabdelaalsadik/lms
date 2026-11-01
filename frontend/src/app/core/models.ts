export type Role = 'Trainee' | 'Instructor' | 'Coordinator' | 'Admin' | 'Auditor';
export type ApiLanguage = 'Ar' | 'En';
export type CourseStatus = 'Draft' | 'Published' | 'Archived';
export type CohortStatus = 'Open' | 'Closed';
export type EnrollmentStatus = 'Pending' | 'Enrolled' | 'Rejected' | 'Withdrawn';

export interface LocalizedText {
  ar: string;
  en: string;
}

export interface CurrentUser {
  id: string;
  maskedNationalId: string;
  fullName: LocalizedText;
  email: string;
  role: Role;
  language: ApiLanguage;
  mfaEnabled: boolean;
  mfaSetupRequired: boolean;
  mustChangePassword: boolean;
}

export interface UserSummary {
  id: string;
  maskedNationalId: string;
  fullName: LocalizedText;
  email: string;
  role: Role;
  createdAt: string;
}

export interface Course {
  id: string;
  code: string;
  title: LocalizedText;
  description: LocalizedText;
  durationHours: number;
  status: CourseStatus;
  createdAt: string;
  publishedAt: string | null;
  categoryId: string | null;
}

export interface UserOption {
  id: string;
  fullName: LocalizedText;
}

export interface Cohort {
  id: string;
  courseId: string;
  courseCode: string;
  courseTitle: LocalizedText;
  name: LocalizedText;
  branch: string;
  instructorId: string;
  instructorName: LocalizedText;
  startsOn: string;
  endsOn: string;
  capacity: number;
  seatsLeft: number;
  pendingCount: number;
  requiresApproval: boolean;
  status: CohortStatus;
  myStatus: EnrollmentStatus | null;
}

export interface RosterEntry {
  enrollmentId: string;
  traineeId: string;
  traineeName: LocalizedText;
  maskedNationalId: string;
  status: EnrollmentStatus;
  requestedAt: string;
  rejectionReason: string | null;
}

export interface CohortRoster {
  cohort: Cohort;
  entries: RosterEntry[];
}

export interface MyEnrollment {
  enrollmentId: string;
  status: EnrollmentStatus;
  rejectionReason: string | null;
  requestedAt: string;
  cohort: Cohort;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

import { LocalizedText } from '../../core/models';

export type ActivityKind = 'Page' | 'File' | 'Video' | 'Scorm' | 'H5p' | 'Form' | 'Book' | 'Lesson';

export interface BookChapter {
  titleAr: string;
  titleEn: string;
  bodyAr: string;
  bodyEn: string;
}

export interface LessonChoice {
  ar: string;
  en: string;
  target: number | null;
}

export interface LessonPage extends BookChapter {
  choices: LessonChoice[];
}
export type FormQuestionKind = 'Single' | 'Multiple' | 'Rating' | 'Text';

export interface FormQuestion {
  kind: FormQuestionKind;
  textAr: string;
  textEn: string;
  isRequired: boolean;
  options: { ar: string; en: string }[];
}

export interface FormDefinition {
  isAnonymous: boolean;
  questions: FormQuestion[];
}
export type AttendanceStatus = 'Present' | 'Absent' | 'Excused';
export type MoveDirection = 'Up' | 'Down';

export interface ContentActivity {
  id: string;
  kind: ActivityKind;
  title: LocalizedText;
  isRequired: boolean;
  bodyAr: string | null;
  bodyEn: string | null;
  videoUrl: string | null;
  fileName: string | null;
  fileSize: number | null;
  form?: FormDefinition | null;
  book?: { chapters: BookChapter[] } | null;
  lesson?: { pages: LessonPage[] } | null;
}

export interface ContentSection {
  id: string;
  title: LocalizedText;
  activities: ContentActivity[];
}

export interface CourseContent {
  courseId: string;
  courseCode: string;
  courseTitle: LocalizedText;
  isSequential: boolean;
  minAttendancePercent: number;
  plannedSessions: number;
  passMarkPercent: number;
  sections: ContentSection[];
}

export interface ActivityInput {
  kind: ActivityKind;
  titleAr: string;
  titleEn: string;
  isRequired: boolean;
  bodyAr: string | null;
  bodyEn: string | null;
  videoUrl: string | null;
  form?: FormDefinition | null;
  book?: { chapters: BookChapter[] } | null;
  lesson?: { pages: LessonPage[] } | null;
}

export interface PlayerActivity extends ContentActivity {
  isCompleted: boolean;
  isLocked: boolean;
}

export interface PlayerSection {
  id: string;
  title: LocalizedText;
  activities: PlayerActivity[];
}

export interface Player {
  cohortId: string;
  courseId: string;
  courseCode: string;
  courseTitle: LocalizedText;
  cohortName: LocalizedText;
  isSequential: boolean;
  resumeActivityId: string | null;
  percent: number;
  remainingActivities: number;
  attendancePercent: number;
  minAttendancePercent: number;
  attendanceMet: boolean;
  passMarkPercent: number;
  finalGradePercent: number | null;
  completedAt: string | null;
  sections: PlayerSection[];
}

export interface MyLearningItem {
  cohortId: string;
  courseCode: string;
  courseTitle: LocalizedText;
  cohortName: LocalizedText;
  startsOn: string;
  endsOn: string;
  hasStarted: boolean;
  percent: number;
  completedAt: string | null;
}

export interface AttendanceEntry {
  traineeId: string;
  status: AttendanceStatus;
}

export interface AttendanceTrainee {
  id: string;
  name: LocalizedText;
  maskedNationalId: string;
}

export interface AttendanceSheet {
  id: string;
  date: string;
  entries: AttendanceEntry[];
}

export interface CohortAttendance {
  startsOn: string;
  endsOn: string;
  trainees: AttendanceTrainee[];
  sessions: AttendanceSheet[];
}

export interface CohortProgress {
  minAttendancePercent: number;
  entries: {
    traineeId: string;
    traineeName: LocalizedText;
    maskedNationalId: string;
    percent: number;
    attendancePercent: number;
    completedAt: string | null;
  }[];
}

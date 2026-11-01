import { LocalizedText } from '../../core/models';

export type QuizStatus = 'Draft' | 'Published';
export type QuestionKind = 'MultipleChoice' | 'TrueFalse' | 'MultipleResponse' | 'ShortAnswer' | 'Numeric' | 'Matching' | 'Essay';
export type GradeItemKind = 'Quiz' | 'Assignment' | 'Workshop';
export type CorrectionStatus = 'Pending' | 'Approved' | 'Rejected';

export interface QuizOption {
  id: string;
  text: LocalizedText;
  isCorrect: boolean;
  matchAr: string | null;
  matchEn: string | null;
}

export interface QuestionDraft {
  kind: QuestionKind;
  textAr: string;
  textEn: string;
  points: number;
  tolerance: number | null;
  options: { textAr: string; textEn: string; isCorrect: boolean; matchAr: string | null; matchEn: string | null }[];
}

export interface BankQuestion extends QuestionDraft {
  id: string;
  category: string;
}

export interface BankList {
  courseId: string;
  categories: string[];
  questions: { items: BankQuestion[]; total: number };
}

export interface QuizQuestion {
  id: string;
  kind: QuestionKind;
  text: LocalizedText;
  points: number;
  tolerance: number | null;
  options: QuizOption[];
}

export interface Quiz {
  id: string;
  title: LocalizedText;
  timeLimitMinutes: number;
  maxAttempts: number;
  opensAt: string | null;
  closesAt: string | null;
  weight: number;
  status: QuizStatus;
  drawCount: number | null;
  shuffle: boolean;
  questions: QuizQuestion[];
  examKey?: string | null;
}

export interface QuizInput {
  titleAr: string;
  titleEn: string;
  timeLimitMinutes: number;
  maxAttempts: number;
  opensAt: string | null;
  closesAt: string | null;
  weight: number;
  drawCount: number | null;
  shuffle: boolean;
  questions: QuestionDraft[];
}

export interface Assignment {
  id: string;
  title: LocalizedText;
  instructions: LocalizedText;
  dueAt: string;
  weight: number;
}

export interface SubmissionEntry {
  id: string;
  traineeId: string;
  traineeName: LocalizedText;
  maskedNationalId: string;
  fileName: string;
  fileSize: number;
  submittedAt: string;
  isLate: boolean;
  feedback: string | null;
  reviewedAt: string | null;
  percent: number | null;
}

export interface Submissions {
  assignment: Assignment;
  gradesPublished: boolean;
  entries: SubmissionEntry[];
}

export interface GradebookItem {
  id: string;
  sourceId: string;
  kind: GradeItemKind;
  title: LocalizedText;
  weight: number;
  isPublished: boolean;
}

export interface GradebookRow {
  traineeId: string;
  name: LocalizedText;
  maskedNationalId: string;
  cells: { itemId: string; percent: number | null }[];
  finalGrade: number | null;
}

export interface Correction {
  id: string;
  itemId: string;
  itemTitle: LocalizedText;
  traineeId: string;
  traineeName: LocalizedText;
  oldPercent: number;
  newPercent: number;
  reason: string;
  status: CorrectionStatus;
  requestedAt: string;
}

export interface Gradebook {
  items: GradebookItem[];
  rows: GradebookRow[];
  corrections: Correction[];
}

export interface AttemptQuestion {
  id: string;
  kind: QuestionKind;
  text: LocalizedText;
  points: number;
  options: { id: string; text: LocalizedText }[];
  choices: { key: string; text: LocalizedText }[];
  response: string | null;
}

export interface Attempt {
  id: string;
  quizId: string;
  title: LocalizedText;
  number: number;
  maxAttempts: number;
  deadlineAt: string;
  secondsLeft: number;
  questions: AttemptQuestion[];
}

export interface MyQuiz {
  id: string;
  title: LocalizedText;
  timeLimitMinutes: number;
  maxAttempts: number;
  attemptsUsed: number;
  isOpen: boolean;
  hasOpenAttempt: boolean;
  closesAt: string | null;
}

export interface MyAssignment {
  id: string;
  title: LocalizedText;
  instructions: LocalizedText;
  dueAt: string;
  fileName: string | null;
  submittedAt: string | null;
  isLate: boolean;
  canSubmit: boolean;
  feedback: string | null;
}

export interface MyAssessments {
  quizzes: MyQuiz[];
  assignments: MyAssignment[];
  grades: { itemId: string; title: LocalizedText; weight: number; percent: number | null }[];
  runningTotal: number | null;
  finalGrade: number | null;
}

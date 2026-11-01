import { FormArray, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { QuestionDraft, QuestionKind } from './assessment.models';

export const QUESTION_KINDS: QuestionKind[] = ['MultipleChoice', 'MultipleResponse', 'TrueFalse', 'ShortAnswer', 'Numeric', 'Matching', 'Essay'];

const TEXT_MAX = 1000;
const OPTION_MAX = 500;

const blank = (kind: QuestionKind): QuestionDraft => {
  const option = (textAr = '', textEn = '', isCorrect = false) => ({ textAr, textEn, isCorrect, matchAr: null, matchEn: null });
  const options = {
    MultipleChoice: [option('', '', true), option()],
    MultipleResponse: [option('', '', true), option()],
    TrueFalse: [option('صحيح', 'True', true), option('خطأ', 'False')],
    ShortAnswer: [option('', '', true)],
    Numeric: [option('', '', true)],
    Matching: [option('', '', true), option('', '', true)],
    Essay: [],
  }[kind];

  return { kind, textAr: '', textEn: '', points: 1, tolerance: kind === 'Numeric' ? 0 : null, options };
};

/** One form shape for every kind, shared by the quiz editor and the question bank. */
export function questionGroup(fb: FormBuilder, source: QuestionDraft | QuestionKind): FormGroup {
  const draft = typeof source === 'string' ? blank(source) : source;

  return fb.nonNullable.group({
    kind: [draft.kind],
    textAr: [draft.textAr, [Validators.required, Validators.maxLength(TEXT_MAX)]],
    textEn: [draft.textEn, [Validators.required, Validators.maxLength(TEXT_MAX)]],
    points: [draft.points, [Validators.required, Validators.min(1)]],
    tolerance: [draft.tolerance ?? 0, [Validators.min(0)]],
    correct: [Math.max(0, draft.options.findIndex((o) => o.isCorrect))],
    options: fb.array(draft.options.map((o) => optionGroup(fb, draft.kind, o))),
  }) as unknown as FormGroup;
}

export function optionGroup(fb: FormBuilder, kind: QuestionKind, option?: QuestionDraft['options'][number]): FormGroup {
  const text = [Validators.required, Validators.maxLength(OPTION_MAX)];
  const match = kind === 'Matching' ? text : [];

  return fb.nonNullable.group({
    // A numeric answer has a single value, so only one field is shown and filled.
    textAr: [option?.textAr ?? '', kind === 'Numeric' ? [] : text],
    textEn: [option?.textEn ?? '', kind === 'Numeric' ? [Validators.required, Validators.pattern(/^\s*-?[\d٠-٩]+([.٫][\d٠-٩]+)?\s*$/)] : text],
    matchAr: [option?.matchAr ?? '', match],
    matchEn: [option?.matchEn ?? '', match],
    isCorrect: [option?.isCorrect ?? kind === 'ShortAnswer'],
  }) as unknown as FormGroup;
}

export function optionsOf(question: FormGroup): FormArray<FormGroup> {
  return question.controls['options'] as FormArray<FormGroup>;
}

export function toDraft(question: FormGroup): QuestionDraft {
  const value = question.getRawValue() as {
    kind: QuestionKind;
    textAr: string;
    textEn: string;
    points: number;
    tolerance: number | null;
    correct: number;
    options: { textAr: string; textEn: string; matchAr: string; matchEn: string; isCorrect: boolean }[];
  };
  const single = value.kind === 'MultipleChoice' || value.kind === 'TrueFalse';

  return {
    kind: value.kind,
    textAr: value.textAr,
    textEn: value.textEn,
    points: value.points,
    tolerance: value.kind === 'Numeric' ? (value.tolerance ?? 0) : null,
    options: value.options.map((option, index) => ({
      textAr: value.kind === 'Numeric' ? option.textEn : option.textAr,
      textEn: option.textEn,
      isCorrect: single ? index === value.correct : value.kind === 'MultipleResponse' ? option.isCorrect : true,
      matchAr: value.kind === 'Matching' ? option.matchAr : null,
      matchEn: value.kind === 'Matching' ? option.matchEn : null,
    })),
  };
}

import { Injectable } from '@angular/core';
import { NativeDateAdapter } from '@angular/material/core';

const ARABIC_DAYS = ['أحد', 'إثنين', 'ثلاثاء', 'أربعاء', 'خميس', 'جمعة', 'سبت'];

/** Calendars show short day names: single Arabic letters are hard to tell apart, and full names don't fit. */
@Injectable()
export class ReadableDateAdapter extends NativeDateAdapter {
  override getDayOfWeekNames(): string[] {
    return String(this.locale).startsWith('ar') ? ARABIC_DAYS : super.getDayOfWeekNames('short');
  }
}

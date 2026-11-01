import { HttpClient } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { ActivityDialog, ActivityDialogResult } from './activity-dialog';
import { ContentActivity, ContentSection, CourseContent, MoveDirection } from './learning.models';
import { LearningService, contentUrl } from './learning.service';
import { SectionDialog } from './section-dialog';

const ICONS = { Page: 'article', File: 'description', Video: 'smart_display', Scorm: 'deployed_code', H5p: 'extension', Form: 'ballot', Book: 'auto_stories', Lesson: 'alt_route' };

@Component({
  selector: 'app-content-editor',
  imports: [
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
    MatTooltipModule,
    ReactiveFormsModule,
    RouterLink,
    StatePanel,
    TranslatePipe,
  ],
  templateUrl: './content-editor.html',
  styleUrl: './content-editor.scss',
})
export class ContentEditor {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly learning = inject(LearningService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly icons = ICONS;
  protected readonly canBackup = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly backupUrl = computed(() => `/api/v1/learning/courses/${this.id()}/backup`);
  protected readonly content = signal<CourseContent | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);

  protected readonly settings = inject(FormBuilder).nonNullable.group({
    isSequential: [false],
    plannedSessions: [0, [Validators.required, Validators.min(0)]],
    minAttendancePercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
    passMarkPercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
  });

  constructor() {
    effect(() => void this.load(this.id()));
  }

  protected async load(courseId: string): Promise<void> {
    this.failed.set(false);

    try {
      this.apply(await firstValueFrom(this.http.get<CourseContent>(contentUrl(courseId))));
    } catch {
      this.failed.set(true);
    }
  }

  protected saveSettings(): Promise<void> {
    if (this.settings.invalid) {
      this.settings.markAllAsTouched();
      return Promise.resolve();
    }

    return this.run(this.learning.saveSettings(this.id(), this.settings.getRawValue()), 'content.settingsSaved');
  }

  protected async addSection(): Promise<void> {
    const title = await this.openSection(null);

    if (title) {
      await this.run(this.learning.addSection(this.id(), title));
    }
  }

  protected async renameSection(section: ContentSection): Promise<void> {
    const title = await this.openSection(section);

    if (title) {
      await this.run(this.learning.renameSection(this.id(), section.id, title));
    }
  }

  protected removeSection(section: ContentSection): Promise<void> {
    return this.confirmThen('content.removeSectionMessage', this.learning.removeSection(this.id(), section.id));
  }

  protected moveSection(section: ContentSection, direction: MoveDirection): Promise<void> {
    return this.run(this.learning.moveSection(this.id(), section.id, direction));
  }

  protected async addActivity(section: ContentSection): Promise<void> {
    const result = await this.openActivity(null);

    if (result) {
      await this.run(
        result.file && (result.input.kind === 'Scorm' || result.input.kind === 'H5p')
          ? this.learning.addScorm(this.id(), section.id, result.input, result.file)
          : result.file
          ? this.learning.addFile(this.id(), section.id, result.input, result.file)
          : this.learning.addActivity(this.id(), section.id, result.input),
      );
    }
  }

  protected async editActivity(activity: ContentActivity): Promise<void> {
    const result = await this.openActivity(activity);

    if (result) {
      await this.run(this.learning.updateActivity(this.id(), activity.id, result.input));
    }
  }

  protected removeActivity(activity: ContentActivity): Promise<void> {
    return this.confirmThen('content.removeActivityMessage', this.learning.removeActivity(this.id(), activity.id));
  }

  protected moveActivity(activity: ContentActivity, direction: MoveDirection): Promise<void> {
    return this.run(this.learning.moveActivity(this.id(), activity.id, direction));
  }

  private async openSection(section: ContentSection | null) {
    const ref = this.dialog.open(SectionDialog, { data: section, width: '28rem', maxWidth: 'calc(100vw - 2rem)' });
    return firstValueFrom(ref.afterClosed()) as Promise<{ titleAr: string; titleEn: string } | undefined>;
  }

  private async openActivity(activity: ContentActivity | null) {
    const ref = this.dialog.open(ActivityDialog, { data: activity, width: '36rem', maxWidth: 'calc(100vw - 2rem)' });
    return firstValueFrom(ref.afterClosed()) as Promise<ActivityDialogResult | undefined>;
  }

  private async confirmThen(messageKey: string, action: Observable<CourseContent>): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'content.removeTitle', messageKey, confirmKey: 'common.remove' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(action);
    }
  }

  private async run(action: Observable<CourseContent>, successKey?: string): Promise<void> {
    this.busy.set(true);

    try {
      this.apply(await firstValueFrom(action));

      if (successKey) {
        this.notify(successKey);
      }
    } catch (error) {
      this.notify(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  private apply(content: CourseContent): void {
    this.content.set(content);
    this.settings.setValue({
      isSequential: content.isSequential,
      plannedSessions: content.plannedSessions,
      minAttendancePercent: content.minAttendancePercent,
      passMarkPercent: content.passMarkPercent,
    });
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

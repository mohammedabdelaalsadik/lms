import { LIVE_CLASSES_ENABLED } from '../../core/features';
import { httpResource } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, effect, inject, input, linkedSignal, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { BookReader, LessonPlayer } from './readers';
import { FormActivity } from './form-activity';
import { Player, PlayerActivity } from './learning.models';
import { LEARNING_URL, LearningService } from './learning.service';

@Component({
  selector: 'app-player',
  imports: [BookReader, FormActivity, LessonPlayer, LocalizedPipe, MatButtonModule, MatIconModule, MatProgressBarModule, RouterLink, StatePanel, TranslatePipe],
  templateUrl: './player.html',
  styleUrl: './player.scss',
})
export class PlayerPage implements OnInit, OnDestroy {
  protected readonly liveClasses = LIVE_CLASSES_ENABLED;
  private readonly sanitizer = inject(DomSanitizer);
  private readonly onMessage = (event: MessageEvent): void => {
    if (event.data?.type === 'lms-scorm-saved') {
      this.resource.reload();
    }
  };

  protected readonly scormUrl = signal<SafeResourceUrl | null>(null);

  ngOnInit(): void {
    window.addEventListener('message', this.onMessage);
  }

  ngOnDestroy(): void {
    window.removeEventListener('message', this.onMessage);
  }

  /** The address comes from our own API and only ever loads inside the sandboxed frame. */
  protected async launch(activity: PlayerActivity): Promise<void> {
    try {
      const { url } = await this.learning.launchScorm(this.cohortId(), activity.id);
      this.scormUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  private readonly closeOnChange = effect(() => {
    this.selectedId();
    this.scormUrl.set(null);
  });

  private readonly learning = inject(LearningService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  protected readonly languages = inject(LanguageService);

  readonly cohortId = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<Player>(() => `${LEARNING_URL}/cohorts/${this.cohortId()}/player`);
  protected readonly player = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  private readonly activities = computed(() => this.player()?.sections.flatMap((s) => s.activities) ?? []);

  /** Opens at the server's resume point, then stays on what the trainee is viewing. */
  protected readonly selectedId = linkedSignal<string | null, string | null>({
    source: () => this.player()?.resumeActivityId ?? null,
    computation: (resume, previous) => previous?.value ?? resume,
  });

  protected readonly selected = computed(
    () => this.activities().find((a) => a.id === this.selectedId()) ?? null,
  );

  protected readonly icon = (activity: PlayerActivity): string => {
    if (activity.isCompleted) {
      return 'check_circle';
    }

    return activity.isLocked ? 'lock' : 'radio_button_unchecked';
  };

  protected body(activity: PlayerActivity): string {
    return (this.languages.language() === 'ar' ? activity.bodyAr : activity.bodyEn) ?? '';
  }

  protected async download(activity: PlayerActivity): Promise<void> {
    try {
      await this.learning.download(activity.id, activity.fileName ?? 'file', this.cohortId());
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  protected async complete(activity: PlayerActivity): Promise<void> {
    this.busy.set(true);

    try {
      await firstValueFrom(this.learning.complete(this.cohortId(), activity.id));
      const list = this.activities();
      const next = list[list.findIndex((a) => a.id === activity.id) + 1];

      if (next) {
        this.selectedId.set(next.id);
      }

      this.resource.reload();
    } catch (error) {
      this.notify(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { urlFilters } from '../../core/filters/url-filters';
import { errorKey } from '../../core/http/api-error';
import { Course, Paged } from '../../core/models';
import { ChoiceChips } from '../../shared/choice-chips';
import { ConfirmData, ConfirmDialog } from '../../shared/confirm-dialog';
import { FilterBar } from '../../shared/filter-bar';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { StatusChip } from '../../shared/status-chip';
import { CATEGORIES_URL, Category, asTree } from './categories-page';
import { CourseFormDialog } from './course-form-dialog';
import { CourseService } from './course.service';

@Component({
  selector: 'app-course-list',
  imports: [
    ChoiceChips,
    FilterBar,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatMenuModule,
    MatPaginatorModule,
    MatSelectModule,
    RouterLink,
    StatePanel,
    StatusChip,
    TranslatePipe,
  ],
  templateUrl: './course-list.html',
  styleUrl: './course-list.scss',
})
export class CourseList {
  private readonly courses = inject(CourseService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);

  protected readonly canManage = computed(() => this.auth.hasRole('Admin', 'Coordinator'));
  protected readonly canEditContent = computed(() =>
    this.auth.hasRole('Admin', 'Coordinator', 'Instructor'),
  );
  protected readonly statusChoices = [
    { value: '', labelKey: 'common.all' },
    { value: 'Draft', labelKey: 'status.Draft' },
    { value: 'Published', labelKey: 'status.Published' },
    { value: 'Archived', labelKey: 'status.Archived' },
  ];
  protected readonly filters = urlFilters({ search: '', status: '', category: '' });
  private readonly categoryResource = httpResource<Category[]>(() => CATEGORIES_URL);
  protected readonly categories = computed(() => (this.categoryResource.hasValue() ? asTree(this.categoryResource.value()) : []));
  protected readonly pageSize = signal(10);

  protected readonly resource = httpResource<Paged<Course>>(() => ({
    url: this.courses.baseUrl,
    params: {
      page: this.filters.page() + 1,
      pageSize: this.pageSize(),
      ...(this.filters.values.search() ? { search: this.filters.values.search() } : {}),
      ...(this.filters.values.status() ? { status: this.filters.values.status() } : {}),
      ...(this.filters.values.category() ? { categoryId: this.filters.values.category() } : {}),
    },
  }));

  protected readonly data = computed(() =>
    this.resource.hasValue() ? this.resource.value() : null,
  );

  protected categoryName(course: Course) {
    return this.categories().find((c) => c.id === course.categoryId)?.name ?? null;
  }

  protected async moveTo(course: Course, categoryId: string | null): Promise<void> {
    await firstValueFrom(this.courses.move(course.id, categoryId));
    this.resource.reload();
  }

  protected onPage(event: PageEvent): void {
    this.filters.setPage(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  protected async openForm(course: Course | null): Promise<void> {
    const ref = this.dialog.open(CourseFormDialog, {
      data: course,
      width: '40rem',
      maxWidth: 'calc(100vw - 2rem)',
    });

    if (await firstValueFrom(ref.afterClosed())) {
      this.notify('courses.saved');
      this.resource.reload();
    }
  }

  protected publish(course: Course): Promise<void> {
    return this.confirmThen(
      {
        titleKey: 'courses.publishTitle',
        messageKey: 'courses.publishMessage',
        confirmKey: 'courses.publish',
      },
      this.courses.publish(course.id),
      'courses.published',
    );
  }

  protected archive(course: Course): Promise<void> {
    return this.confirmThen(
      {
        titleKey: 'courses.archiveTitle',
        messageKey: 'courses.archiveMessage',
        confirmKey: 'courses.archive',
      },
      this.courses.archive(course.id),
      'courses.archived',
    );
  }

  private async confirmThen(
    data: ConfirmData,
    action: Observable<void>,
    successKey: string,
  ): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, { data, width: '26rem' });

    if (!(await firstValueFrom(ref.afterClosed()))) {
      return;
    }

    try {
      await firstValueFrom(action);
      this.notify(successKey);
      this.resource.reload();
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}

import { NgTemplateOutlet } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { LocalizedText } from '../../core/models';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';

interface Person {
  traineeId: string;
  name: LocalizedText;
}

interface Group {
  id: string;
  name: string;
  members: Person[];
}

interface Board {
  groups: Group[];
  unassigned: Person[];
}

@Component({
  selector: 'app-cohort-groups',
  imports: [LocalizedPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, NgTemplateOutlet, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/cohorts', id()]" class="back"><mat-icon class="flip-rtl">arrow_back</mat-icon>{{ 'cohorts.roster' | translate }}</a>

      <header class="page-header">
        <div>
          <h1>{{ 'groups.title' | translate }}</h1>
          <p>{{ 'groups.subtitle' | translate }}</p>
        </div>
      </header>

      <section class="panel tools">
        <form (submit)="$event.preventDefault(); create(name)">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'groups.name' | translate }}</mat-label>
            <input #name matInput maxlength="100" />
          </mat-form-field>
          <button matButton="filled" type="submit" [disabled]="busy()">{{ 'groups.add' | translate }}</button>
        </form>
        <form (submit)="$event.preventDefault(); split(count.value)">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'groups.count' | translate }}</mat-label>
            <input #count matInput type="number" min="1" max="50" value="2" />
          </mat-form-field>
          <button matButton="outlined" type="submit" [disabled]="busy()">{{ 'groups.split' | translate }}</button>
        </form>
      </section>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <div class="board">
          @for (group of data()!.groups; track group.id) {
            <section class="panel column">
              <header>
                <h2>{{ group.name }}</h2>
                <button matIconButton [attr.aria-label]="'common.remove' | translate" (click)="remove(group)"><mat-icon>delete</mat-icon></button>
              </header>
              @for (person of group.members; track person.traineeId) {
                <ng-container *ngTemplateOutlet="row; context: { person, current: group.id }" />
              } @empty {
                <p class="none">{{ 'groups.noMembers' | translate }}</p>
              }
            </section>
          }

          <section class="panel column">
            <header><h2>{{ 'groups.unassigned' | translate }}</h2></header>
            @for (person of data()!.unassigned; track person.traineeId) {
              <ng-container *ngTemplateOutlet="row; context: { person, current: '' }" />
            } @empty {
              <p class="none">{{ 'groups.allPlaced' | translate }}</p>
            }
          </section>
        </div>
      }

      <ng-template #row let-person="person" let-current="current">
        <div class="person">
          <span>{{ person.name | localized }}</span>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'groups.moveTo' | translate }}</mat-label>
            <mat-select [value]="current" (selectionChange)="place(person, $event.value)">
              <mat-option value="">{{ 'groups.unassigned' | translate }}</mat-option>
              @for (group of data()!.groups; track group.id) {
                <mat-option [value]="group.id">{{ group.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>
      </ng-template>
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      padding: var(--space-4);

      form {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-2);
      }
    }
    .board {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
      gap: var(--space-4);
      align-items: start;
    }
    .column {
      display: grid;
      gap: var(--space-2);
      padding: var(--space-3);

      header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        min-height: 2.5rem;
      }
    }
    h2 {
      margin: 0;
      font: var(--mat-sys-title-medium);
    }
    .person {
      display: grid;
      gap: var(--space-1);
    }
    .none {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class CohortGroups {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly resource = httpResource<Board>(() => this.url());
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  private url(): string {
    return `/api/v1/cohorts/${this.id()}/groups`;
  }

  protected async create(box: HTMLInputElement): Promise<void> {
    if (box.value.trim() && (await this.run(this.http.post(this.url(), { name: box.value })))) {
      box.value = '';
    }
  }

  protected async split(count: string): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'groups.splitTitle', messageKey: 'groups.splitMessage', confirmKey: 'groups.split' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.http.post(`${this.url()}/split`, { count: Number(count) }));
    }
  }

  protected place(person: Person, groupId: string): Promise<boolean> {
    return this.run(this.http.put(`${this.url()}/members/${person.traineeId}`, { groupId: groupId || null }));
  }

  protected async remove(group: Group): Promise<void> {
    const ref = this.dialog.open(ConfirmDialog, {
      width: '26rem',
      data: { titleKey: 'groups.removeTitle', messageKey: 'groups.removeMessage', confirmKey: 'common.remove' },
    });

    if (await firstValueFrom(ref.afterClosed())) {
      await this.run(this.http.delete(`${this.url()}/${group.id}`));
    }
  }

  private async run(call: ReturnType<HttpClient['get']>): Promise<boolean> {
    this.busy.set(true);

    try {
      await firstValueFrom(call);
      this.resource.reload();
      return true;
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 5000 });
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}

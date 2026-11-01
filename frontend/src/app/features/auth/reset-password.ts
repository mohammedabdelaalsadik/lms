import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { AuthLayout } from './auth-layout';

/** Without a token it asks for the National ID. With one it takes the new password. */
@Component({
  selector: 'app-reset-password',
  imports: [AuthLayout, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, RouterLink, TranslatePipe],
  template: `
    <app-auth-layout>
      <form class="auth-form" (ngSubmit)="submit()">
        <header>
          <h1>{{ (token ? 'reset.newTitle' : 'reset.title') | translate }}</h1>
          @if (!done()) {
            <p>{{ (token ? 'reset.newHelp' : 'reset.help') | translate }}</p>
          }
        </header>

        @if (error(); as key) {
          <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
        }

        @if (done()) {
          <p role="status">{{ (token ? 'reset.changed' : 'reset.sent') | translate }}</p>
        } @else if (token) {
          <mat-form-field>
            <mat-label>{{ 'security.newPassword' | translate }}</mat-label>
            <input matInput type="password" class="ltr-value" name="password" autocomplete="new-password" minlength="10" maxlength="128" [ngModel]="password()" (ngModelChange)="password.set($event)" />
            <mat-hint>{{ 'security.passwordHint' | translate }}</mat-hint>
          </mat-form-field>
          <button matButton="filled" type="submit" [disabled]="busy() || password().length < 10">{{ 'common.save' | translate }}</button>
        } @else {
          <mat-form-field>
            <mat-label>{{ 'fields.nationalId' | translate }}</mat-label>
            <input matInput class="ltr-value" name="nationalId" inputmode="numeric" autocomplete="username" maxlength="10" [ngModel]="nationalId()" (ngModelChange)="nationalId.set($event)" />
          </mat-form-field>
          <button matButton="filled" type="submit" [disabled]="busy() || !nationalId().trim()">{{ 'reset.send' | translate }}</button>
        }

        <p class="switch"><a routerLink="/login">{{ 'auth.login' | translate }}</a></p>
      </form>
    </app-auth-layout>
  `,
  styleUrl: './auth-form.scss',
})
export class ResetPassword {
  private readonly http = inject(HttpClient);

  protected readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
  protected readonly nationalId = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(
        this.token
          ? this.http.post('/api/v1/auth/password/reset', { token: this.token, newPassword: this.password() })
          : this.http.post('/api/v1/auth/password/forgot', { nationalId: this.nationalId().trim() }),
      );
      this.done.set(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}

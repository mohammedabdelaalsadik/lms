import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { CurrentUser } from '../../core/models';

interface MfaSetup {
  secret: string;
  qrDataUrl: string;
}

const MFA_URL = '/api/v1/auth/mfa';

@Component({
  selector: 'app-security-page',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'security.title' | translate }}</h1>
          <p>{{ 'security.subtitle' | translate }}</p>
        </div>
      </header>

      @if (auth.user()?.mustChangePassword) {
        <div class="notice" role="status"><mat-icon aria-hidden="true">key</mat-icon>{{ 'security.passwordRequired' | translate }}</div>
      } @else if (auth.user()?.mfaSetupRequired && !codes()) {
        <div class="notice" role="status"><mat-icon aria-hidden="true">shield</mat-icon>{{ 'security.required' | translate }}</div>
      }

      @if (error(); as key) {
        <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
      }

      <section class="panel body">
        <h2>{{ 'security.passwordTitle' | translate }}</h2>
        <form class="password" (ngSubmit)="changePassword()">
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'security.password' | translate }}</mat-label>
            <input matInput type="password" name="current" autocomplete="current-password" [ngModel]="current()" (ngModelChange)="current.set($event)" />
          </mat-form-field>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'security.newPassword' | translate }}</mat-label>
            <input matInput type="password" name="next" autocomplete="new-password" minlength="10" maxlength="128" [ngModel]="next()" (ngModelChange)="next.set($event)" />
            <mat-hint>{{ 'security.passwordHint' | translate }}</mat-hint>
          </mat-form-field>
          <button matButton="outlined" type="submit" [disabled]="busy() || !current() || next().length < 10">{{ 'security.changePassword' | translate }}</button>
        </form>
        @if (changed()) {
          <p role="status" class="hint">{{ 'security.passwordChanged' | translate }}</p>
        }
      </section>

      <section class="panel body">
        <h2>{{ 'security.stepTitle' | translate }}</h2>
        @if (codes(); as list) {
          <h2>{{ 'security.codesTitle' | translate }}</h2>
          <p>{{ 'security.codesHelp' | translate }}</p>
          <ol class="codes ltr-value">
            @for (code of list; track code) {
              <li>{{ code }}</li>
            }
          </ol>
          <button matButton="filled" (click)="finish()">{{ 'security.codesDone' | translate }}</button>
        } @else if (setup(); as details) {
          <p>{{ 'security.scan' | translate }}</p>
          <img [src]="details.qrDataUrl" width="220" height="220" [alt]="'security.scan' | translate" />
          <p>{{ 'security.manual' | translate }}: <code class="ltr-value">{{ details.secret }}</code></p>
          <form (ngSubmit)="enable()">
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'security.code' | translate }}</mat-label>
              <input matInput class="ltr-value" name="code" inputmode="numeric" autocomplete="one-time-code" maxlength="6" [ngModel]="code()" (ngModelChange)="code.set($event)" />
            </mat-form-field>
            <button matButton="filled" type="submit" [disabled]="busy() || code().trim().length !== 6">{{ 'security.confirm' | translate }}</button>
          </form>
        } @else if (auth.user()?.mfaEnabled) {
          <p class="state on"><mat-icon aria-hidden="true">verified_user</mat-icon>{{ 'security.on' | translate }}</p>
          @if (auth.user()?.role !== 'Trainee') {
            <p class="hint">{{ 'security.mustKeep' | translate }}</p>
          }
          <form (ngSubmit)="disable()">
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'security.password' | translate }}</mat-label>
              <input matInput type="password" name="password" autocomplete="current-password" [ngModel]="password()" (ngModelChange)="password.set($event)" />
            </mat-form-field>
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'security.code' | translate }}</mat-label>
              <input matInput class="ltr-value" name="off-code" inputmode="numeric" autocomplete="one-time-code" maxlength="6" [ngModel]="code()" (ngModelChange)="code.set($event)" />
            </mat-form-field>
            <button matButton="outlined" type="submit" [disabled]="busy() || !password() || code().trim().length !== 6">{{ 'security.turnOff' | translate }}</button>
          </form>
        } @else {
          <p class="state"><mat-icon aria-hidden="true">shield</mat-icon>{{ 'security.off' | translate }}</p>
          <button matButton="filled" [disabled]="busy()" (click)="begin()">{{ 'security.turnOn' | translate }}</button>
        }
      </section>
    </div>
  `,
  styles: `
    .body {
      display: grid;
      justify-items: start;
      gap: var(--space-3);
      padding: var(--space-4);
    }
    h2,
    p {
      margin: 0;
    }
    .notice {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-3) var(--space-4);
      border-radius: var(--radius-m);
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .state {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      font-weight: 500;
    }
    .state.on mat-icon {
      color: var(--brand-primary);
    }
    .hint {
      color: var(--mat-sys-on-surface-variant);
    }
    img {
      border-radius: var(--radius-s);
      background: #fff;
    }
    code {
      overflow-wrap: anywhere;
    }
    form {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
    .codes {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 10rem));
      gap: var(--space-2) var(--space-4);
      margin: 0;
      padding: 0;
      font-family: ui-monospace, monospace;
      list-style: none;
    }
  `,
})
export class SecurityPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly auth = inject(AuthService);
  protected readonly setup = signal<MfaSetup | null>(null);
  protected readonly codes = signal<string[] | null>(null);
  protected readonly code = signal('');
  protected readonly password = signal('');
  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly changed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected changePassword(): Promise<void> {
    return this.run(async () => {
      const user = await firstValueFrom(this.http.post<CurrentUser>('/api/v1/auth/password', { currentPassword: this.current(), newPassword: this.next() }));

      this.auth.user.set(user);
      this.current.set('');
      this.next.set('');
      this.changed.set(true);

      if (!user.mfaSetupRequired) {
        await this.router.navigateByUrl('/dashboard');
      }
    });
  }

  protected begin(): Promise<void> {
    return this.run(async () => this.setup.set(await firstValueFrom(this.http.post<MfaSetup>(`${MFA_URL}/setup`, {}))));
  }

  protected enable(): Promise<void> {
    return this.run(async () => {
      const reply = await firstValueFrom(
        this.http.post<{ user: CurrentUser; recoveryCodes: string[] }>(`${MFA_URL}/enable`, { code: this.code().trim() }),
      );

      this.setup.set(null);
      this.codes.set(reply.recoveryCodes);
      this.auth.user.set(reply.user);
    });
  }

  protected disable(): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.http.post<void>(`${MFA_URL}/disable`, { password: this.password(), code: this.code().trim() }));
      this.password.set('');
      await this.auth.restore();
    });
  }

  protected finish(): void {
    this.codes.set(null);
    void this.router.navigateByUrl('/dashboard');
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await work();
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.code.set('');
      this.busy.set(false);
    }
  }
}

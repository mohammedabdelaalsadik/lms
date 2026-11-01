import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiLanguage, CurrentUser, Role } from '../models';
import { AppLanguage, LanguageService } from '../i18n/language.service';

export interface RegisterRequest {
  nationalId: string;
  fullNameAr: string;
  fullNameEn: string;
  email: string;
  password: string;
  language: ApiLanguage;
}

const toApiLanguage = (language: AppLanguage): ApiLanguage => (language === 'ar' ? 'Ar' : 'En');

interface MfaStep {
  mfaRequired: true;
  challenge: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly languages = inject(LanguageService);

  readonly user = signal<CurrentUser | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);

  private renewal: Promise<boolean> | null = null;

  /** Rebuilds the session from the HttpOnly cookie. No session is a normal outcome here. */
  async restore(): Promise<void> {
    try {
      this.user.set(await firstValueFrom(this.http.get<CurrentUser>('/api/v1/auth/me')));
    } catch {
      this.user.set(null);
    }
  }

  /** Returns a challenge when a second step is needed, otherwise the session has started. */
  async login(nationalId: string, password: string): Promise<string | null> {
    const reply = await firstValueFrom(
      this.http.post<CurrentUser | MfaStep>('/api/v1/auth/login', { nationalId, password }),
    );

    if ('mfaRequired' in reply) {
      return reply.challenge;
    }

    await this.begin(reply);
    return null;
  }

  async verifyMfa(challenge: string, code: string): Promise<void> {
    await this.begin(await firstValueFrom(this.http.post<CurrentUser>('/api/v1/auth/mfa/verify', { challenge, code })));
  }

  private async begin(user: CurrentUser): Promise<void> {
    this.user.set(user);
    await this.languages.use(user.language === 'Ar' ? 'ar' : 'en');
  }

  /** Renews the access token once, however many calls failed at the same moment. */
  renewSession(): Promise<boolean> {
    this.renewal ??= firstValueFrom(this.http.post<CurrentUser>('/api/v1/auth/refresh', {}))
      .then(() => true)
      .catch(() => false)
      .finally(() => (this.renewal = null));

    return this.renewal;
  }

  register(request: RegisterRequest): Promise<CurrentUser> {
    return firstValueFrom(this.http.post<CurrentUser>('/api/v1/auth/register', request));
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/v1/auth/logout', {}));
    this.clear();
  }

  async changeLanguage(language: AppLanguage): Promise<void> {
    await this.languages.use(language);

    if (this.isAuthenticated()) {
      await firstValueFrom(
        this.http.put<void>('/api/v1/auth/me/language', { language: toApiLanguage(language) }),
      );
    }
  }

  clear(): void {
    this.user.set(null);
  }

  hasRole(...roles: Role[]): boolean {
    const user = this.user();
    return user !== null && roles.includes(user.role);
  }
}

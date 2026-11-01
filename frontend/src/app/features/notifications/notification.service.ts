import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LocalizedText, Paged } from '../../core/models';

export interface AppNotification {
  id: string;
  kind: string;
  title: LocalizedText;
  body: LocalizedText;
  link: string;
  createdAt: string;
  isRead: boolean;
}

export const NOTIFICATIONS_URL = '/api/v1/notifications';

const ICONS: Record<string, string> = {
  Welcome: 'waving_hand',
  EnrollmentConfirmed: 'how_to_reg',
  EnrollmentRejected: 'cancel',
  GradesPublished: 'grading',
  GradeCorrected: 'edit_note',
  CertificateIssued: 'workspace_premium',
  CertificateRevoked: 'gpp_bad',
  LearningPlanAssigned: 'route',
  PrivacyRequestReceived: 'shield_person',
  PrivacyRequestDecided: 'shield_person',
  LiveClassScheduled: 'videocam',
  NewMessage: 'chat',
  ForumReply: 'forum',
};

export const notificationIcon = (kind: string): string => ICONS[kind] ?? 'notifications';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);

  readonly unread = signal(0);

  async refreshCount(): Promise<void> {
    try {
      const { count } = await firstValueFrom(this.http.get<{ count: number }>(`${NOTIFICATIONS_URL}/unread-count`));
      this.unread.set(count);
    } catch {
      // The badge is a convenience. It tries again on the next pass.
    }
  }

  latest(): Promise<Paged<AppNotification>> {
    return firstValueFrom(this.http.get<Paged<AppNotification>>(NOTIFICATIONS_URL, { params: { pageSize: 6 } }));
  }

  async markRead(id: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${NOTIFICATIONS_URL}/${id}/read`, {}));
    await this.refreshCount();
  }

  async markAllRead(): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${NOTIFICATIONS_URL}/read-all`, {}));
    this.unread.set(0);
  }
}

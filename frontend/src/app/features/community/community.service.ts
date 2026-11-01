import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LocalizedText, Paged } from '../../core/models';

export const FORUMS_URL = '/api/v1/forums';
export const MESSAGES_URL = '/api/v1/messages';

export interface ForumCohort {
  cohortId: string;
  name: LocalizedText;
  startsOn: string;
  endsOn: string;
}

export interface TopicSummary {
  id: string;
  title: string;
  authorName: LocalizedText;
  createdAt: string;
  lastActivityAt: string;
  replyCount: number;
  isPinned: boolean;
  isLocked: boolean;
  groupId: string | null;
}

export interface TopicList {
  cohortName: LocalizedText;
  canModerate: boolean;
  groups: { id: string; name: string }[];
  topics: Paged<TopicSummary>;
}

export interface ForumPost {
  id: string;
  authorId: string;
  authorName: LocalizedText;
  body: string | null;
  createdAt: string;
  isRemoved: boolean;
  canRemove: boolean;
}

export interface TopicDetail {
  id: string;
  title: string;
  body: string;
  authorId: string;
  authorName: LocalizedText;
  createdAt: string;
  isPinned: boolean;
  isLocked: boolean;
  canModerate: boolean;
  canRemove: boolean;
  posts: ForumPost[];
}

export interface ConversationSummary {
  userId: string;
  name: LocalizedText;
  role: string;
  lastMessage: string;
  lastMessageAt: string;
  unread: number;
}

export interface Contact {
  id: string;
  name: LocalizedText;
  role: string;
}

export interface ChatMessage {
  id: string;
  isMine: boolean;
  body: string;
  sentAt: string;
  isRead: boolean;
}

export interface Thread {
  userId: string;
  name: LocalizedText;
  role: string;
  messages: ChatMessage[];
}

@Injectable({ providedIn: 'root' })
export class CommunityService {
  private readonly http = inject(HttpClient);

  readonly unreadMessages = signal(0);

  startTopic(cohortId: string, title: string, body: string, groupId: string | null): Promise<string> {
    return firstValueFrom(this.http.post<string>(`${FORUMS_URL}/${cohortId}/topics`, { title, body, groupId }));
  }

  reply(cohortId: string, topicId: string, body: string): Promise<string> {
    return firstValueFrom(this.http.post<string>(`${FORUMS_URL}/${cohortId}/topics/${topicId}/posts`, { body }));
  }

  moderate(cohortId: string, topicId: string, change: { pinned?: boolean; locked?: boolean }): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${FORUMS_URL}/${cohortId}/topics/${topicId}/moderation`, change));
  }

  removeTopic(cohortId: string, topicId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${FORUMS_URL}/${cohortId}/topics/${topicId}`));
  }

  removePost(cohortId: string, topicId: string, postId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${FORUMS_URL}/${cohortId}/topics/${topicId}/posts/${postId}`));
  }

  send(userId: string, body: string): Promise<string> {
    return firstValueFrom(this.http.post<string>(`${MESSAGES_URL}/${userId}`, { body }));
  }

  async markRead(userId: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${MESSAGES_URL}/${userId}/read`, {}));
    await this.refreshUnread();
  }

  async refreshUnread(): Promise<void> {
    try {
      const { count } = await firstValueFrom(this.http.get<{ count: number }>(`${MESSAGES_URL}/unread-count`));
      this.unreadMessages.set(count);
    } catch {
      // The badge is a convenience. It tries again on the next pass.
    }
  }
}

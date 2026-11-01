import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { map } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { LanguageService } from '../core/i18n/language.service';
import { Role } from '../core/models';
import { MessagesButton } from '../features/community/messages-button';
import { NotificationBell } from '../features/notifications/notification-bell';
import { BrandMark } from '../shared/brand-mark';
import { LocalizedPipe } from '../shared/localized.pipe';
import { ToolbarControls } from '../shared/toolbar-controls';

interface NavItem {
  path: string;
  icon: string;
  labelKey: string;
  roles?: Role[];
}

interface NavGroup {
  labelKey: string;
  items: NavItem[];
}

const CLOSED_KEY = 'lms.nav.closed';
const RAIL_KEY = 'lms.nav.rail';

function readRail(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) === 'true';
  } catch {
    return false;
  }
}

function readClosed(): string[] {
  try {
    const stored: unknown = JSON.parse(localStorage.getItem(CLOSED_KEY) ?? '[]');
    return Array.isArray(stored) ? stored.filter((v): v is string => typeof v === 'string') : [];
  } catch {
    return [];
  }
}

const NAV_GROUPS: NavGroup[] = [
  {
    labelKey: 'nav.groupMain',
    items: [
      { path: '/dashboard', icon: 'space_dashboard', labelKey: 'nav.dashboard' },
      { path: '/calendar', icon: 'calendar_month', labelKey: 'nav.calendar' },
      { path: '/messages', icon: 'chat', labelKey: 'messages.title' },
      { path: '/my-files', icon: 'folder', labelKey: 'files.title' },
      { path: '/privacy', icon: 'shield_person', labelKey: 'privacy.title' },
      { path: '/forum', icon: 'forum', labelKey: 'forum.title', roles: ['Admin', 'Coordinator', 'Instructor', 'Trainee'] },
    ],
  },
  {
    labelKey: 'nav.groupLearning',
    items: [
      { path: '/learn', icon: 'school', labelKey: 'nav.learn', roles: ['Trainee'] },
      { path: '/courses', icon: 'menu_book', labelKey: 'nav.courses' },
      { path: '/cohorts', icon: 'groups', labelKey: 'nav.cohorts' },
      { path: '/my-enrollments', icon: 'assignment_turned_in', labelKey: 'nav.myEnrollments', roles: ['Trainee'] },
      { path: '/certificates', icon: 'workspace_premium', labelKey: 'nav.certificates', roles: ['Trainee'] },
      { path: '/my-badges', icon: 'military_tech', labelKey: 'badges.mine', roles: ['Trainee'] },
      { path: '/my-plans', icon: 'route', labelKey: 'plans.mine', roles: ['Trainee'] },
    ],
  },
  {
    labelKey: 'nav.groupAdmin',
    items: [
      { path: '/certificates/manage', icon: 'workspace_premium', labelKey: 'nav.certificatesManage', roles: ['Admin', 'Coordinator', 'Auditor'] },
      { path: '/reports', icon: 'bar_chart', labelKey: 'reports.title', roles: ['Admin', 'Coordinator', 'Instructor', 'Auditor'] },
      { path: '/badges', icon: 'military_tech', labelKey: 'badges.title', roles: ['Admin', 'Coordinator'] },
      { path: '/competencies', icon: 'psychology', labelKey: 'competencies.title', roles: ['Admin', 'Coordinator', 'Auditor'] },
      { path: '/plans', icon: 'route', labelKey: 'plans.title', roles: ['Admin', 'Coordinator', 'Auditor'] },
      { path: '/categories', icon: 'category', labelKey: 'categories.title', roles: ['Admin', 'Coordinator'] },
      { path: '/users', icon: 'group', labelKey: 'nav.users', roles: ['Admin', 'Auditor'] },
      { path: '/users/online', icon: 'wifi_tethering', labelKey: 'online.title', roles: ['Admin', 'Auditor'] },
      { path: '/privacy/requests', icon: 'rule', labelKey: 'privacy.queueTitle', roles: ['Admin', 'Auditor'] },
      { path: '/audit', icon: 'policy', labelKey: 'nav.audit', roles: ['Admin', 'Auditor'] },
    ],
  },
];

@Component({
  selector: 'app-shell',
  imports: [
    BrandMark,
    LocalizedPipe,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatSidenavModule,
    MessagesButton,
    NotificationBell,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    ToolbarControls,
    TranslatePipe,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly router = inject(Router);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly translate = inject(TranslateService);
  private readonly languages = inject(LanguageService);
  protected readonly auth = inject(AuthService);
  protected readonly filter = signal('');
  private readonly closed = signal(readClosed());

  protected readonly isCompact = toSignal(
    this.breakpoints.observe('(max-width: 959px)').pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  private readonly railChosen = signal(readRail());

  /** Icons only on a wide screen. A phone always gets the full menu in its drawer. */
  protected readonly isRail = computed(() => this.railChosen() && !this.isCompact());

  protected readonly navGroups = computed(() =>
    NAV_GROUPS.map((group) => ({
      ...group,
      items: group.items.filter((item) => !item.roles || this.auth.hasRole(...item.roles)),
    })).filter((group) => group.items.length > 0),
  );

  /** Typing narrows the menu, and a match always shows even inside a closed group. */
  protected readonly shownGroups = computed(() => {
    const term = this.filter().trim().toLowerCase();
    this.languages.language();

    return this.navGroups()
      .map((group) => ({
        ...group,
        items: term ? group.items.filter((item) => (this.translate.instant(item.labelKey) as string).toLowerCase().includes(term)) : group.items,
      }))
      .filter((group) => group.items.length > 0);
  });

  protected readonly initial = computed(() => {
    const name = this.auth.user()?.fullName;
    return (this.languages.language() === 'ar' ? name?.ar : name?.en)?.trim().charAt(0) ?? '';
  });

  protected isOpen(group: NavGroup): boolean {
    return this.filter().trim().length > 0 || !this.closed().includes(group.labelKey);
  }

  protected toggle(group: NavGroup): void {
    this.closed.update((list) => (list.includes(group.labelKey) ? list.filter((key) => key !== group.labelKey) : [...list, group.labelKey]));

    try {
      localStorage.setItem(CLOSED_KEY, JSON.stringify(this.closed()));
    } catch {
      // The menu still works. It just forgets on the next visit.
    }
  }

  protected toggleRail(): void {
    this.railChosen.update((rail) => !rail);
    this.filter.set('');

    try {
      localStorage.setItem(RAIL_KEY, String(this.railChosen()));
    } catch {
      // The menu still works. It just forgets on the next visit.
    }
  }

  /** Enter in the box searches courses, cohorts and people, not only the menu. */
  protected searchEverything(drawer: MatSidenav): void {
    const term = this.filter().trim();

    if (term.length >= 2) {
      this.filter.set('');
      this.closeIfCompact(drawer);
      void this.router.navigate(['/search'], { queryParams: { q: term } });
    }
  }

  protected closeIfCompact(drawer: MatSidenav): void {
    if (this.isCompact()) {
      void drawer.close();
    }
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}

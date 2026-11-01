import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { CERTIFICATES_URL, Certificate, CertificateService } from './certificate.service';

@Component({
  selector: 'app-my-certificates',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <header class="page-header">
        <div>
          <h1>{{ 'nav.certificates' | translate }}</h1>
          <p>{{ 'certificates.subtitle' | translate }}</p>
        </div>
      </header>

      <section class="panel">
        @if (resource.error()) {
          <app-state-panel kind="error" (retry)="resource.reload()" />
        } @else if (items() === null) {
          <app-state-panel kind="loading" />
        } @else if (items()!.length === 0) {
          <app-state-panel kind="empty" />
        } @else {
          <ul>
            @for (certificate of items(); track certificate.id) {
              <li>
                <mat-icon class="seal" aria-hidden="true">workspace_premium</mat-icon>
                <div class="main">
                  <h2>{{ certificate.courseTitle | localized }}</h2>
                  <p>{{ 'certificates.completedOn' | translate: { date: (certificate.completedOn | appDate) } }}</p>
                  <p class="ltr-value">{{ certificate.code }}</p>
                </div>
                @if (certificate.status === 'Issued') {
                  <div class="tools">
                    <a matButton [routerLink]="['/verify', certificate.code]">{{ 'certificates.verifyLink' | translate }}</a>
                    <button matButton="filled" (click)="download(certificate)">
                      <mat-icon>download</mat-icon>
                      {{ 'certificates.download' | translate }}
                    </button>
                  </div>
                } @else {
                  <span class="revoked">{{ 'certificateStatus.Revoked' | translate }}</span>
                }
              </li>
            }
          </ul>
        }
      </section>
    </div>
  `,
  styles: `
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3) var(--space-4);
      padding: var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    .seal {
      color: var(--brand-accent);
    }
    .main {
      flex: 1 1 14rem;
      min-width: 0;
    }
    h2 {
      font: var(--mat-sys-title-medium);
      font-weight: 600;
    }
    p {
      color: var(--mat-sys-on-surface-variant);
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
    .revoked {
      color: var(--mat-sys-error);
      font: var(--mat-sys-label-large);
    }
  `,
})
export class MyCertificates {
  private readonly certificates = inject(CertificateService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  protected readonly resource = httpResource<Certificate[]>(() => `${CERTIFICATES_URL}/my`);
  protected readonly items = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected async download(certificate: Certificate): Promise<void> {
    try {
      await this.certificates.download(certificate);
    } catch (error) {
      this.snackBar.open(this.translate.instant(errorKey(error)), undefined, { duration: 4000 });
    }
  }
}

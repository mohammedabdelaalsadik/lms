import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LocalizedText } from '../../core/models';

export type CertificateStatus = 'Issued' | 'Revoked';

export interface Certificate {
  id: string;
  code: string;
  traineeName: LocalizedText;
  courseCode: string;
  courseTitle: LocalizedText;
  completedOn: string;
  issuedAt: string;
  status: CertificateStatus;
  revokedAt: string | null;
  revocationReason: string | null;
  replacedById: string | null;
}

export interface CertificateVerification {
  code: string;
  status: CertificateStatus;
  traineeName: LocalizedText;
  courseCode: string;
  courseTitle: LocalizedText;
  completedOn: string;
  issuedAt: string;
  revokedAt: string | null;
}

export const CERTIFICATES_URL = '/api/v1/certificates';

@Injectable({ providedIn: 'root' })
export class CertificateService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  revoke(id: string, reason: string) {
    return this.http.post<void>(`${CERTIFICATES_URL}/${id}/revoke`, { reason });
  }

  reissue(id: string) {
    return this.http.post<Certificate>(`${CERTIFICATES_URL}/${id}/reissue`, {});
  }

  verify(code: string) {
    return this.http.get<CertificateVerification>(`${CERTIFICATES_URL}/verify/${encodeURIComponent(code)}`);
  }

  /** Goes through HttpClient so an expired session renews before the download. */
  async download(certificate: Certificate): Promise<void> {
    const blob = await firstValueFrom(this.http.get(`${CERTIFICATES_URL}/${certificate.id}/pdf`, { responseType: 'blob' }));
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = `certificate-${certificate.code}.pdf`;
    link.click();
    URL.revokeObjectURL(url);
  }
}

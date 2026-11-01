import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** Goes through HttpClient so an expired session renews before the download. */
export async function downloadFile(http: HttpClient, url: string, fileName: string, params: Record<string, string> = {}): Promise<void> {
  const blob = await firstValueFrom(http.get(url, { params, responseType: 'blob' }));
  const link = document.createElement('a');
  const address = URL.createObjectURL(blob);

  link.href = address;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(address);
}

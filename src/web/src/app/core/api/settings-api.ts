import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ConnectionStatus, ConnectionTestResult } from './api-types';

/** The read-only settings page's API (Admin only, D26). */
@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  connections(): Observable<ConnectionStatus[]> {
    return this.http.get<ConnectionStatus[]>('/api/settings/connections');
  }

  test(kind: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(`/api/settings/connections/${kind}/test`, null);
  }
}

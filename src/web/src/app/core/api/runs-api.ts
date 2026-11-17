import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { RunBody, ScanStep } from './api-types';

/** Run endpoints: the report (or status while unfinished), steps and cancel. */
@Injectable({ providedIn: 'root' })
export class RunsApi {
  private readonly http = inject(HttpClient);

  get(runId: string): Observable<RunBody> {
    return this.http.get<RunBody>(`/api/runs/${runId}`);
  }

  steps(runId: string): Observable<ScanStep[]> {
    return this.http.get<ScanStep[]>(`/api/runs/${runId}/steps`);
  }

  cancel(runId: string): Observable<void> {
    return this.http.post(`/api/runs/${runId}/cancel`, null).pipe(map(() => undefined));
  }
}

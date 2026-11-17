import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { JiraCreate, JiraDraft, RemediationAction } from './api-types';

/** Jira tickets from findings (doc 09, D24): preview, create, and the repository's actions with live status. */
@Injectable({ providedIn: 'root' })
export class RemediationApi {
  private readonly http = inject(HttpClient);

  jiraPreview(runId: string, findingId: string): Observable<JiraDraft> {
    return this.http.post<JiraDraft>(`/api/runs/${runId}/findings/${findingId}/jira-preview`, null);
  }

  createJira(runId: string, findingId: string, body: JiraCreate): Observable<RemediationAction> {
    return this.http.post<RemediationAction>(`/api/runs/${runId}/findings/${findingId}/jira`, body);
  }

  actions(repositoryId: string): Observable<RemediationAction[]> {
    return this.http.get<RemediationAction[]>(`/api/repositories/${repositoryId}/actions`);
  }
}

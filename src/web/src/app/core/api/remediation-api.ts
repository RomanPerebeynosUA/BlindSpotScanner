import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Features, FixCreate, FixDraft, JiraCreate, JiraDraft, RemediationAction } from './api-types';

/** Jira tickets and fix PRs from findings (doc 09, D24, D25), the repository's actions with live status, and the feature flags. */
@Injectable({ providedIn: 'root' })
export class RemediationApi {
  private readonly http = inject(HttpClient);

  jiraPreview(runId: string, findingId: string): Observable<JiraDraft> {
    return this.http.post<JiraDraft>(`/api/runs/${runId}/findings/${findingId}/jira-preview`, null);
  }

  createJira(runId: string, findingId: string, body: JiraCreate): Observable<RemediationAction> {
    return this.http.post<RemediationAction>(`/api/runs/${runId}/findings/${findingId}/jira`, body);
  }

  fixPreview(runId: string, findingId: string): Observable<FixDraft> {
    return this.http.post<FixDraft>(`/api/runs/${runId}/findings/${findingId}/fix-preview`, null);
  }

  createPullRequest(runId: string, findingId: string, body: FixCreate): Observable<RemediationAction> {
    return this.http.post<RemediationAction>(`/api/runs/${runId}/findings/${findingId}/pull-request`, body);
  }

  features(): Observable<Features> {
    return this.http.get<Features>('/api/features');
  }

  actions(repositoryId: string): Observable<RemediationAction[]> {
    return this.http.get<RemediationAction[]>(`/api/repositories/${repositoryId}/actions`);
  }
}

import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Profile, ProfileDraft, ProfileInput, Repository, RepositorySummary, RunHistoryItem } from './api-types';

/** Repository, profile and scan-start endpoints (contracts/openapi.yaml, D22). */
@Injectable({ providedIn: 'root' })
export class RepositoriesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<RepositorySummary[]> {
    return this.http.get<RepositorySummary[]>('/api/repositories');
  }

  add(url: string, defaultBranch?: string): Observable<Repository> {
    return this.http.post<Repository>('/api/repositories', { url, defaultBranch });
  }

  get(id: string): Observable<Repository> {
    return this.http.get<Repository>(`/api/repositories/${id}`);
  }

  runs(id: string): Observable<RunHistoryItem[]> {
    return this.http.get<RunHistoryItem[]>(`/api/repositories/${id}/runs`);
  }

  startScan(id: string, branch?: string): Observable<{ runId: string }> {
    return this.http.post<{ runId: string }>(`/api/repositories/${id}/scans`, branch ? { branch } : {});
  }

  profile(id: string, version?: number): Observable<Profile> {
    const query = version === undefined ? '' : `?version=${version}`;
    return this.http.get<Profile>(`/api/repositories/${id}/profile${query}`);
  }

  profileVersions(id: string): Observable<number[]> {
    return this.http.get<number[]>(`/api/repositories/${id}/profile/versions`);
  }

  saveProfile(id: string, baseVersion: number, input: ProfileInput): Observable<Profile> {
    return this.http.put<Profile>(`/api/repositories/${id}/profile`, { baseVersion, ...input });
  }

  draftProfile(id: string): Observable<ProfileDraft> {
    return this.http.post<ProfileDraft>(`/api/repositories/${id}/profile/draft`, null);
  }
}

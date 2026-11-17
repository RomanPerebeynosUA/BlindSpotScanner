import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RemediationApi } from './remediation-api';
import { JiraCreate } from './api-types';

describe('RemediationApi', () => {
  let api: RemediationApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(RemediationApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('jiraPreview posts /api/runs/{id}/findings/{fid}/jira-preview', () => {
    // Act
    api.jiraPreview('run1', 'f2').subscribe();

    // Assert
    expect(http.expectOne('/api/runs/run1/findings/f2/jira-preview').request.method).toBe('POST');
  });

  it('createJira posts the body to /api/runs/{id}/findings/{fid}/jira', () => {
    // Arrange
    const body: JiraCreate = { summary: 's', description: 'd', projectKey: 'BSS', issueType: 'Task', priority: 'High', labels: ['integration-coverage'], force: false };

    // Act
    api.createJira('run1', 'f2', body).subscribe();

    // Assert
    const request = http.expectOne('/api/runs/run1/findings/f2/jira').request;
    expect([request.method, request.body]).toEqual(['POST', body]);
  });

  it('actions gets /api/repositories/{id}/actions', () => {
    // Act
    api.actions('repo1').subscribe();

    // Assert
    expect(http.expectOne('/api/repositories/repo1/actions').request.method).toBe('GET');
  });
});

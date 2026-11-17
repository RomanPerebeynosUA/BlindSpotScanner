import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RemediationApi } from './remediation-api';
import { FixCreate, JiraCreate } from './api-types';

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

  it('fixPreview posts /api/runs/{id}/findings/{fid}/fix-preview', () => {
    // Act
    api.fixPreview('run1', 'f2').subscribe();

    // Assert
    expect(http.expectOne('/api/runs/run1/findings/f2/fix-preview').request.method).toBe('POST');
  });

  it('fixPreview with regenerate asks for the default branch head', () => {
    // Act
    api.fixPreview('run1', 'f2', true).subscribe();

    // Assert
    expect(http.expectOne('/api/runs/run1/findings/f2/fix-preview?regenerate=true').request.method).toBe('POST');
  });

  it('createPullRequest posts the body to /api/runs/{id}/findings/{fid}/pull-request', () => {
    // Arrange
    const body: FixCreate = { files: [{ path: 'tests/A.cs', action: 'create', content: 'class A {}' }], branch: 'coverage/fix-x', title: 't', body: 'b', force: false, baseSha: 'abc' };

    // Act
    api.createPullRequest('run1', 'f2', body).subscribe();

    // Assert
    const request = http.expectOne('/api/runs/run1/findings/f2/pull-request').request;
    expect([request.method, request.body]).toEqual(['POST', body]);
  });

  it('features gets /api/features', () => {
    // Act
    api.features().subscribe();

    // Assert
    expect(http.expectOne('/api/features').request.method).toBe('GET');
  });

  it('actions gets /api/repositories/{id}/actions', () => {
    // Act
    api.actions('repo1').subscribe();

    // Assert
    expect(http.expectOne('/api/repositories/repo1/actions').request.method).toBe('GET');
  });
});

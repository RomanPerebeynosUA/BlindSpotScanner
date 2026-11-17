import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RunsApi } from './runs-api';
import { isReport, RunBody } from './api-types';

describe('RunsApi', () => {
  let api: RunsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(RunsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('cancel posts /api/runs/{id}/cancel', () => {
    // Act
    api.cancel('run1').subscribe();

    // Assert
    expect(http.expectOne('/api/runs/run1/cancel').request.method).toBe('POST');
  });

  it('steps gets /api/runs/{id}/steps', () => {
    // Act
    api.steps('run1').subscribe();

    // Assert
    expect(http.expectOne('/api/runs/run1/steps').request.method).toBe('GET');
  });

  it('isReport tells a report from a status body', () => {
    // Arrange
    const status: RunBody = { runId: 'r', status: 'Running', failureReasons: [] };
    const report = { meta: {} } as RunBody;

    // Act
    const result = [isReport(status), isReport(report)];

    // Assert
    expect(result).toEqual([false, true]);
  });
});

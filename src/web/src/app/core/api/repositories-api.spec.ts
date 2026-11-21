import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RepositoriesApi } from './repositories-api';
import { ProfileInput } from './api-types';

describe('RepositoriesApi', () => {
  let api: RepositoriesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(RepositoriesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('add posts url and branch to /api/repositories', () => {
    // Act
    api.add('https://github.com/a/b', 'dev').subscribe();

    // Assert
    const request = http.expectOne('/api/repositories');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ url: 'https://github.com/a/b', defaultBranch: 'dev' });
  });

  it('remove sends DELETE to the repository', () => {
    // Act
    api.remove('r1').subscribe();

    // Assert
    const request = http.expectOne('/api/repositories/r1');
    expect(request.request.method).toBe('DELETE');
  });

  it('saveProfile puts baseVersion with the fields', () => {
    // Arrange
    const input = { markdown: '# P', integrationMappedTo: 'real-seam', testGlobs: ['t'] } as ProfileInput;

    // Act
    api.saveProfile('r1', 3, input).subscribe();

    // Assert
    const request = http.expectOne('/api/repositories/r1/profile');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ baseVersion: 3, ...input });
  });

  it('profile with a version asks for that version', () => {
    // Act
    api.profile('r1', 2).subscribe();

    // Assert
    expect(http.expectOne('/api/repositories/r1/profile?version=2').request.method).toBe('GET');
  });

  it('startScan posts an empty body to the scans endpoint', () => {
    // Act
    api.startScan('r1').subscribe();

    // Assert
    const request = http.expectOne('/api/repositories/r1/scans');
    expect(request.request.body).toEqual({});
  });
});

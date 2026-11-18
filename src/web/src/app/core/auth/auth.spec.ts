import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService, BROWSER_LOCATION, hasRole } from './auth';

describe('AuthService', () => {
  const location = { href: '', pathname: '/runs/run1', search: '?x=1' };

  beforeEach(() => {
    location.href = '';
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: BROWSER_LOCATION, useValue: location }] });
  });

  it('can() respects role order', () => {
    // Act
    const member = { name: 'dev', roles: ['Member'], localMode: false };

    // Assert
    expect([hasRole(member, 'Viewer'), hasRole(member, 'Member'), hasRole(member, 'Admin'), hasRole(null, 'Viewer')]).toEqual([true, true, false, false]);
  });

  it('load() with 401 leaves me null', async () => {
    // Arrange
    const auth = TestBed.inject(AuthService);
    const loading = auth.load();

    // Act
    TestBed.inject(HttpTestingController).expectOne('/api/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await loading;

    // Assert
    expect(auth.me()).toBeNull();
  });

  it('load() keeps the signed-in user', async () => {
    // Arrange
    const auth = TestBed.inject(AuthService);
    const loading = auth.load();

    // Act
    TestBed.inject(HttpTestingController).expectOne('/api/me').flush({ name: 'dev@example.com', roles: ['Member'], localMode: false });
    await loading;

    // Assert
    expect([auth.me()?.name, auth.can('Member'), auth.can('Admin')]).toEqual(['dev@example.com', true, false]);
  });

  it('login() sends the browser to sign-in with the current path', () => {
    // Act
    TestBed.inject(AuthService).login();

    // Assert
    expect(location.href).toBe('/api/auth/login?returnUrl=%2Fruns%2Frun1%3Fx%3D1');
  });

  it('load() with a server error marks the server unreachable, not signed out', async () => {
    // Arrange
    const auth = TestBed.inject(AuthService);
    const loading = auth.load();

    // Act
    TestBed.inject(HttpTestingController).expectOne('/api/me').flush(null, { status: 502, statusText: 'Bad Gateway' });
    await loading;

    // Assert
    expect([auth.me(), auth.unreachable()]).toEqual([undefined, true]);
  });
});

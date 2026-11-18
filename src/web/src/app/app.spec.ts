import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { provideTestAuth } from '../testing/auth';
import { AuthService, Me } from './core/auth/auth';

describe('App', () => {
  async function configure(role: 'Viewer' | 'Member' | 'Admin' | null = 'Admin', user: Partial<Me> = {}) {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideTestAuth(role, user)],
    }).compileComponents();
  }

  beforeEach(async () => {
    await configure();
  });

  it('shell renders the Repositories nav link', async () => {
    // Arrange
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    const link = (fixture.nativeElement as HTMLElement).querySelector('nav.rail .links a');
    expect(link?.querySelector('.label')?.textContent?.trim()).toBe('Repositories');
    expect(link?.getAttribute('href')).toBe('/');
  });

  it('theme toggle sets data-theme on the html element', async () => {
    // Arrange
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const html = document.documentElement;
    html.removeAttribute('data-theme');

    // Act
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.theme')!.click();

    // Assert
    expect(['light', 'dark']).toContain(html.getAttribute('data-theme'));
  });

  it('account card shows initials, name, role and Settings for an Admin', async () => {
    // Arrange
    TestBed.resetTestingModule();
    await configure('Admin', { name: 'Dev Example' });
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect([
      el.querySelector('.account .avatar')?.textContent?.trim(),
      el.querySelector('.account .name')?.textContent?.trim(),
      el.querySelector('.account .role')?.textContent?.trim(),
      el.querySelector('nav.rail a[href="/settings"]') !== null,
    ]).toEqual(['DE', 'Dev Example', 'Admin', true]);
  });

  it('local mode explains itself instead of offering Sign out', async () => {
    // Arrange
    TestBed.resetTestingModule();
    await configure('Admin', { name: 'local-admin', localMode: true });
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect([
      el.querySelector('.account .name')?.textContent?.trim(),
      el.querySelector('.account .mode-note')?.textContent?.trim(),
      el.querySelector('button.signout'),
    ]).toEqual(['Local admin', 'Local mode: everyone is an admin', null]);
  });

  it('signed-in users get a named Sign out button', async () => {
    // Arrange
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    expect((fixture.nativeElement as HTMLElement).querySelector('button.signout')?.getAttribute('aria-label')).toBe('Sign out');
  });

  it('theme toggle names what it will do', async () => {
    // Arrange
    document.documentElement.setAttribute('data-theme', 'dark');
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    expect((fixture.nativeElement as HTMLElement).querySelector('button.theme')?.getAttribute('aria-label')).toBe('Switch to light theme');
  });

  it('anonymous shows Sign in instead of the app', async () => {
    // Arrange
    TestBed.resetTestingModule();
    await configure(null);
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect([el.querySelector('button.signin')?.textContent?.trim(), el.querySelector('router-outlet')]).toEqual(['Sign in with your organization account', null]);
  });

  it('an unreachable server shows a retry, not Sign in', async () => {
    // Arrange
    TestBed.resetTestingModule();
    await configure(null);
    const auth = TestBed.inject(AuthService);
    auth.me.set(undefined);
    auth.unreachable.set(true);
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect([el.textContent?.includes("Can't reach the server"), el.querySelector('button.retry') !== null, el.querySelector('button.signin')]).toEqual([true, true, null]);
  });
});

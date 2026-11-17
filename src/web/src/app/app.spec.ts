import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { provideTestAuth } from '../testing/auth';

describe('App', () => {
  async function configure(role: 'Viewer' | 'Member' | 'Admin' | null = 'Admin') {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideTestAuth(role)],
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
    const link = (fixture.nativeElement as HTMLElement).querySelector('nav.rail a');
    expect(link?.textContent?.trim()).toBe('Repositories');
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

  it('user bar shows the name and Settings for an Admin', async () => {
    // Arrange
    const fixture = TestBed.createComponent(App);

    // Act
    await fixture.whenStable();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect([el.querySelector('.user .name')?.textContent?.trim(), el.querySelector('nav.rail a[href="/settings"]') !== null]).toEqual(['test-user', true]);
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
    expect([el.querySelector('button.signin')?.textContent?.trim(), el.querySelector('router-outlet')]).toEqual(['Sign in', null]);
  });
});

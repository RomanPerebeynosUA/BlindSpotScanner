import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
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
});

import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { SettingsApi } from '../../core/api/settings-api';
import { ConnectionStatus } from '../../core/api/api-types';
import { Settings } from './settings';

const statuses: ConnectionStatus[] = [
  { kind: 'github', configured: true, masked: '••••abcd', details: { apiBaseUrl: 'https://api.github.com' } },
  { kind: 'jira', configured: false, masked: null, details: { baseUrl: '', email: '', projectKey: '', issueType: 'Task' } },
  { kind: 'llm', configured: true, masked: '••••wxyz', details: { model: 'claude-sonnet-5-5', maxCostUsdPerScan: '5', promptSetVersion: '1.0.0' } },
];

describe('Settings', () => {
  let test: ReturnType<typeof vi.fn>;

  async function render() {
    test = vi.fn(() => of({ ok: true, message: 'Connected to GitHub as octocat.' }));
    TestBed.configureTestingModule({ imports: [Settings], providers: [{ provide: SettingsApi, useValue: { connections: () => of(statuses), test } }] });
    const fixture = TestBed.createComponent(Settings);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const card = (el: HTMLElement, kind: string) => el.querySelector(`[data-kind=${kind}]`) as HTMLElement;

  it('lists each connection with its masked secret', async () => {
    // Act
    const { el } = await render();

    // Assert
    expect([
      card(el, 'github').querySelector('.masked')?.textContent?.trim(),
      card(el, 'jira').querySelector('.state')?.textContent?.trim(),
      card(el, 'llm').textContent?.includes('claude-sonnet-5-5'),
    ]).toEqual(['••••abcd', 'Not configured', true]);
  });

  it('Test shows the result', async () => {
    // Arrange
    const { fixture, el } = await render();

    // Act
    (card(el, 'github').querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect([test.mock.calls[0][0], card(el, 'github').querySelector('.result')?.textContent?.trim()]).toEqual(['github', 'Connected to GitHub as octocat.']);
  });
});

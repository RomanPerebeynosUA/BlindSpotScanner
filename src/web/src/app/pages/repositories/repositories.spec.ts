import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { RepositorySummary } from '../../core/api/api-types';
import { Repositories } from './repositories';

const summary = (overrides: Partial<RepositorySummary> = {}): RepositorySummary => ({
  id: 'r1',
  fullName: 'example-org/bookstore-api',
  defaultBranch: 'main',
  lastRunId: 'run1',
  lastScore: 5.1,
  lastCoveredShare: 0.6,
  lastScanAt: '2026-11-15T09:00:00Z',
  activeRunId: null,
  latestRunStatus: 'Succeeded',
  trend: [],
  ...overrides,
});

async function render(list: RepositorySummary[]): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    imports: [Repositories],
    providers: [provideRouter([]), { provide: RepositoriesApi, useValue: { list: () => of(list) } }],
  });
  const fixture = TestBed.createComponent(Repositories);
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const cells = (el: HTMLElement): string[] => [...el.querySelectorAll('tbody tr td')].map((td) => td.textContent?.trim() ?? '');

describe('Repositories', () => {
  it('lists repositories with score and covered share', async () => {
    // Act
    const el = await render([summary()]);

    // Assert
    const row = cells(el);
    expect(row[0]).toContain('example-org/bookstore-api');
    expect(row[2]).toBe('5.1/10');
    expect(row[3]).toBe('60%');
  });

  it('null score shows a dash', async () => {
    // Act
    const el = await render([summary({ lastScore: null, lastCoveredShare: null, lastScanAt: null, lastRunId: null })]);

    // Assert
    expect(cells(el).slice(1, 4)).toEqual(['—', '—', '—']);
  });

  it('active run shows Scanning and a Watch link', async () => {
    // Act
    const el = await render([summary({ activeRunId: 'run9' })]);

    // Assert
    expect(cells(el)[5]).toBe('Scanning');
    const link = el.querySelector('tbody a.btn') as HTMLAnchorElement;
    expect(link.textContent?.trim()).toBe('Watch');
    expect(link.getAttribute('href')).toBe('/runs/run9/live');
  });

  it('idle repository links to its overview', async () => {
    // Act
    const el = await render([summary()]);

    // Assert
    expect(el.querySelector('tbody a.btn')?.getAttribute('href')).toBe('/repos/r1');
  });

  it('only failed scans show Last scan failed', async () => {
    // Act
    const el = await render([summary({ lastRunId: null, lastScore: null, lastCoveredShare: null, lastScanAt: null, latestRunStatus: 'Failed' })]);

    // Assert
    expect(cells(el)[5]).toBe('Last scan failed');
  });

  it('repositories trend column shows a sparkline for two or more points', async () => {
    // Act
    const el = await render([summary({ trend: [
      { runId: 'a', generatedAt: '2026-11-01T09:00:00Z', score: 4.7, coveredShare: 0.4 },
      { runId: 'b', generatedAt: '2026-11-15T09:00:00Z', score: 5.1, coveredShare: 0.6 },
    ] })]);

    // Assert
    expect(el.querySelector('tbody td.trend svg')?.getAttribute('aria-label')).toBe('Score trend: 4.7 to 5.1');
  });

  it('repositories trend with one point shows a dash', async () => {
    // Act
    const el = await render([summary({ trend: [{ runId: 'b', generatedAt: '2026-11-15T09:00:00Z', score: 5.1, coveredShare: 0.6 }] })]);

    // Assert
    expect(el.querySelector('tbody td.trend')?.textContent?.trim()).toBe('—');
  });

  it('empty list shows the empty state', async () => {
    // Act
    const el = await render([]);

    // Assert
    expect(el.textContent).toContain('No repositories yet. Add one to start scanning.');
  });
});

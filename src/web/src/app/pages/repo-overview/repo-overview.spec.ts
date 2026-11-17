import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { NEVER, of, Observable } from 'rxjs';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { RunHistoryItem } from '../../core/api/api-types';
import { RepoOverview } from './repo-overview';

const run = (overrides: Partial<RunHistoryItem> = {}): RunHistoryItem => ({
  runId: 'run1',
  status: 'Succeeded',
  branch: 'main',
  commitSha: 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678',
  queuedAt: '2026-11-15T08:59:00Z',
  generatedAt: '2026-11-15T09:00:00Z',
  score: 5.1,
  weightAssessablePct: 90,
  endpointsTotal: 5,
  endpointsCovered: 3,
  endpointsWeak: 1,
  endpointsNone: 1,
  endpointsUnverified: 0,
  costUsd: 0.91,
  failureReasons: [],
  ...overrides,
});

const repository = { id: 'r1', owner: 'example-org', name: 'bookstore', fullName: 'example-org/bookstore', defaultBranch: 'main', htmlUrl: 'https://github.com/example-org/bookstore' };

async function render(runs: RunHistoryItem[], startScan: () => Observable<{ runId: string }> = () => of({ runId: 'new' })) {
  TestBed.configureTestingModule({
    imports: [RepoOverview],
    providers: [
      provideRouter([]),
      { provide: RepositoriesApi, useValue: { get: () => of(repository), runs: () => of(runs), startScan } },
    ],
  });
  const fixture = TestBed.createComponent(RepoOverview);
  fixture.componentRef.setInput('id', 'r1');
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

const rows = (el: HTMLElement) => [...el.querySelectorAll('tbody tr')] as HTMLElement[];

describe('RepoOverview', () => {
  it('shows run history newest first as returned', async () => {
    // Act
    const { el } = await render([run({ runId: 'b', commitSha: 'bbbbbbbbbb' }), run({ runId: 'a', commitSha: 'aaaaaaaaaa' })]);

    // Assert
    expect(rows(el).map((r) => r.querySelector('.sha')?.textContent?.trim())).toEqual(['bbbbbbb', 'aaaaaaa']);
  });

  it('running row links to the live page', async () => {
    // Act
    const { el } = await render([run({ status: 'Running', generatedAt: null, score: null, commitSha: '' })]);

    // Assert
    expect(rows(el)[0].querySelector('a')?.getAttribute('href')).toBe('/runs/run1/live');
  });

  it('succeeded row links to the report with covered over total', async () => {
    // Act
    const { el } = await render([run()]);

    // Assert
    expect(rows(el)[0].querySelector('a')?.getAttribute('href')).toBe('/runs/run1');
    expect(rows(el)[0].textContent).toContain('3 / 5');
  });

  it('succeeded null score shows N/A', async () => {
    // Act
    const { el } = await render([run({ score: null })]);

    // Assert
    expect(rows(el)[0].querySelector('.score')?.textContent?.trim()).toBe('N/A');
  });

  it('failed row shows its first reason', async () => {
    // Act
    const { el } = await render([run({ status: 'Failed', score: null, failureReasons: ['Unresolved citation: x.cs::Y', 'second'] })]);

    // Assert
    expect(rows(el)[0].textContent).toContain('Unresolved citation: x.cs::Y');
    expect(rows(el)[0].textContent).not.toContain('second');
  });

  it('Scan now starts a scan and navigates live', async () => {
    // Arrange
    const { fixture, el } = await render([]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    // Act
    (el.querySelector('button.primary') as HTMLButtonElement).click();
    await fixture.whenStable();

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/runs', 'new', 'live']);
  });

  it('Scan now disabled while starting', async () => {
    // Arrange
    const { fixture, el } = await render([], () => NEVER);
    const button = el.querySelector('button.primary') as HTMLButtonElement;

    // Act
    button.click();
    fixture.detectChanges();

    // Assert
    expect(button.disabled).toBe(true);
  });

  it('overview shows score and covered share trends oldest first', async () => {
    // Act (history is newest first; the failed run has no headline)
    const { el } = await render([
      run({ runId: 'c', score: 5.1, endpointsCovered: 3, endpointsTotal: 5 }),
      run({ runId: 'f', status: 'Failed', score: null, endpointsCovered: null, endpointsTotal: null }),
      run({ runId: 'a', score: 4.7, endpointsCovered: 2, endpointsTotal: 5 }),
    ]);

    // Assert
    const labels = [...el.querySelectorAll('.trends svg')].map((s) => s.getAttribute('aria-label'));
    expect(labels).toEqual(['Score: 4.7 to 5.1', 'Covered share: 40% to 60%']);
  });

  it('no runs shows the empty state', async () => {
    // Act
    const { el } = await render([]);

    // Assert
    expect(el.textContent).toContain('No scans yet.');
  });
});

import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { NEVER, of, throwError, Observable } from 'rxjs';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { RunHistoryItem } from '../../core/api/api-types';
import { RepoOverview } from './repo-overview';
import { MatDialog } from '@angular/material/dialog';
import { provideTestAuth } from '../../../testing/auth';

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

const repository = { id: 'r1', owner: 'example-org', name: 'bookstore', fullName: 'example-org/bookstore', defaultBranch: 'main', htmlUrl: 'https://github.com/example-org/bookstore', llmSharingAcknowledged: true };

const dialogOpen = vi.fn();
const acknowledgeLlmSharing = vi.fn(() => of(undefined));
const remove = vi.fn((): Observable<void> => of(undefined));

async function render(
  runs: RunHistoryItem[],
  startScan: () => Observable<{ runId: string }> = () => of({ runId: 'new' }),
  options: { role?: 'Viewer' | 'Member' | 'Admin'; acknowledged?: boolean; consent?: boolean | 'pending'; loadRepository?: boolean } = {},
) {
  remove.mockClear();
  dialogOpen.mockReset().mockReturnValue({ afterClosed: () => (options.consent === 'pending' ? NEVER : of(options.consent ?? false)) });
  const repo = { ...repository, llmSharingAcknowledged: options.acknowledged ?? true };
  TestBed.configureTestingModule({
    imports: [RepoOverview],
    providers: [
      provideRouter([]),
      provideTestAuth(options.role ?? 'Member'),
      { provide: MatDialog, useValue: { open: dialogOpen } },
      { provide: RepositoriesApi, useValue: { get: () => (options.loadRepository === false ? NEVER : of(repo)), runs: () => of(runs), startScan, acknowledgeLlmSharing, remove } },
    ],
  });
  vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(RepoOverview);
  fixture.componentRef.setInput('id', 'r1');
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

const deleteButton = (el: HTMLElement) => el.querySelector('button.delete-repo') as HTMLButtonElement | null;

const rows = (el: HTMLElement) => [...el.querySelectorAll('tbody tr')] as HTMLElement[];

describe('RepoOverview', () => {
  it('links back to all repositories', async () => {
    // Act
    const { el } = await render([run()]);

    // Assert
    const back = el.querySelector('a.back') as HTMLAnchorElement;
    expect({ text: back.textContent?.trim(), href: back.getAttribute('href') }).toEqual({ text: 'All repositories', href: '/' });
  });

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

  it('overview trend keeps the last 12 succeeded runs like the repositories list', async () => {
    // Act (13 runs, newest first: scores 5.2 down to 4.0)
    const scores = [5.2, 5.1, 5.0, 4.9, 4.8, 4.7, 4.6, 4.5, 4.4, 4.3, 4.2, 4.1, 4.0];
    const { el } = await render(scores.map((score, i) => run({ runId: `r${i}`, score })));

    // Assert
    expect(el.querySelector('.trends svg')?.getAttribute('aria-label')).toBe('Score: 4.1 to 5.2');
  });

  it('no runs shows the empty state', async () => {
    // Act
    const { el } = await render([]);

    // Assert
    expect(el.textContent).toContain('No scans yet.');
  });

  it('Viewer sees no Scan now button', async () => {
    // Act
    const { el } = await render([], undefined, { role: 'Viewer' });

    // Assert
    expect([...el.querySelectorAll('button')].some((b) => b.textContent?.includes('Scan now'))).toBe(false);
  });

  it('unacknowledged Scan now asks for consent and scans after it', async () => {
    // Arrange
    const startScan = vi.fn(() => of({ runId: 'new' }));
    const { el } = await render([], startScan, { acknowledged: false, consent: true });

    // Act
    ([...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Scan now')) as HTMLButtonElement).click();

    // Assert
    expect([dialogOpen.mock.calls.length, acknowledgeLlmSharing.mock.calls.length, startScan.mock.calls.length]).toEqual([1, 1, 1]);
  });

  it('declined consent starts no scan', async () => {
    // Arrange
    const startScan = vi.fn(() => of({ runId: 'new' }));
    const { el } = await render([], startScan, { acknowledged: false, consent: false });

    // Act
    ([...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Scan now')) as HTMLButtonElement).click();

    // Assert
    expect(startScan).not.toHaveBeenCalled();
  });

  it('a second click while the consent dialog is open opens no second dialog', async () => {
    // Arrange
    const { el } = await render([], undefined, { acknowledged: false, consent: 'pending' });
    const scan = [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Scan now')) as HTMLButtonElement;

    // Act
    scan.click();
    scan.click();

    // Assert
    expect(dialogOpen).toHaveBeenCalledTimes(1);
  });

  it('Scan now waits until the repository is loaded', async () => {
    // Act (without it, the consent check can't run)
    const { el } = await render([], undefined, { loadRepository: false });

    // Assert
    const scan = [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Scan now')) as HTMLButtonElement;
    expect(scan.disabled).toBe(true);
  });

  it('hides Delete repository from members', async () => {
    // Act
    const { el } = await render([run()], undefined, { role: 'Member' });

    // Assert
    expect(deleteButton(el)).toBeNull();
  });

  it('deletes the repository once confirmed and returns to the list', async () => {
    // Arrange
    const { fixture, el } = await render([run()], undefined, { role: 'Admin', consent: true });

    // Act
    deleteButton(el)!.click();
    await fixture.whenStable();

    // Assert
    expect({ removed: remove.mock.calls, to: vi.mocked(TestBed.inject(Router).navigate).mock.calls.at(-1) }).toEqual({ removed: [['r1']], to: [['/']] });
  });

  it('keeps the repository when the confirmation is declined', async () => {
    // Arrange
    const { fixture, el } = await render([run()], undefined, { role: 'Admin', consent: false });

    // Act
    deleteButton(el)!.click();
    await fixture.whenStable();

    // Assert
    expect(remove).not.toHaveBeenCalled();
  });

  it('stays on the page and re-enables Delete when the server refuses', async () => {
    // Arrange
    const { fixture, el } = await render([run()], undefined, { role: 'Admin', consent: true });
    remove.mockReturnValueOnce(throwError(() => new Error('refused')));

    // Act
    deleteButton(el)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    expect({ disabled: deleteButton(el)!.disabled, navigated: vi.mocked(TestBed.inject(Router).navigate).mock.calls.length }).toEqual({ disabled: false, navigated: 0 });
  });
});

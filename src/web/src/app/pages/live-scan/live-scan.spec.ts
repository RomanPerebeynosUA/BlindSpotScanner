import { provideTestAuth } from '../../../testing/auth';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, Observable, NEVER, Subject, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { RunsApi } from '../../core/api/runs-api';
import { RunBody, RunStatus, ScanStep } from '../../core/api/api-types';
import { ScanProgress, ScanProgressService } from '../../core/scan-progress';
import { LiveScan } from './live-scan';

const running: RunBody = { runId: 'run1', status: 'Running', failureReasons: [] };
const report = { meta: { runId: 'run1' } } as RunBody;
const step = (phase: string, status: RunStatus, notes: string | null = null): ScanStep => ({
  phase, status, startedAt: '2026-11-17T10:00:00Z', completedAt: null, inputTokens: 0, outputTokens: 0, costUsd: 0, readInFull: 0, notes,
});

describe('LiveScan', () => {
  const events = signal<ScanProgress[]>([]);
  const completed = signal<RunStatus | null>(null);
  const stop = vi.fn().mockResolvedValue(undefined);
  const navigate = vi.fn().mockResolvedValue(true);
  const cancel = vi.fn();
  let get: () => Observable<RunBody>;
  let steps: ScanStep[];

  beforeEach(() => {
    events.set([]);
    completed.set(null);
    stop.mockClear();
    navigate.mockClear();
    cancel.mockReset();
    get = () => of(running);
    steps = [];
  });

  afterEach(() => vi.useRealTimers());

  async function render() {
    TestBed.configureTestingModule({
      imports: [LiveScan],
      providers: [
        provideTestAuth(),
        provideRouter([]),
        { provide: RunsApi, useValue: { get: () => get(), steps: () => of(steps), cancel, repository: () => of({ id: 'repo1' }) } },
        { provide: ScanProgressService, useValue: { watch: () => ({ events, completed, stop }) } },
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(navigate);
    const fixture = TestBed.createComponent(LiveScan);
    fixture.componentRef.setInput('runId', 'run1');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, el, settle };
  }

  const state = (el: HTMLElement, phase: string) => el.querySelector(`.step[data-phase="${phase}"]`)?.getAttribute('data-state');
  const progress = (phase: string, pct: number, message: string): ScanProgress => ({ runId: 'run1', phase, pct, message, at: new Date(2026, 10, 17, 14, 2, 1) });

  it('links back to the repository scans', async () => {
    // Act
    const { el } = await render();

    // Assert
    const back = el.querySelector('a.back') as HTMLAnchorElement;
    expect({ text: back.textContent?.trim(), href: back.getAttribute('href') }).toEqual({ text: 'All scans', href: '/repos/repo1' });
  });

  it('seeds the stepper from steps', async () => {
    // Arrange
    steps = [step('P1', 'Succeeded'), step('P2', 'Failed', 'tree read failed')];

    // Act
    const { el } = await render();

    // Assert
    expect([state(el, 'P1'), state(el, 'P2'), state(el, 'P3')]).toEqual(['done', 'failed', 'waiting']);
    expect(el.querySelector('.step[data-phase="P2"]')?.textContent).toContain('tree read failed');
  });

  it('marks a phase running then done from events', async () => {
    // Arrange
    const { el, settle } = await render();

    // Act
    events.set([progress('P1', 0, 'P1 started')]);
    await settle();
    const first = state(el, 'P1');
    events.set([progress('P1', 0, 'P1 started'), progress('P1', 12, 'P1 done')]);
    await settle();

    // Assert
    expect([first, state(el, 'P1')]).toEqual(['running', 'done']);
  });

  it('shows log lines with time', async () => {
    // Arrange
    const { el, settle } = await render();

    // Act
    events.set([progress('P1', 0, 'P1 started')]);
    await settle();

    // Assert
    expect(el.querySelector('.log')?.textContent).toContain('14:02:01 P1 started');
  });

  it('cancel disables and shows Cancelling', async () => {
    // Arrange
    cancel.mockReturnValue(NEVER);
    const { el, settle } = await render();
    const button = el.querySelector('button.cancel') as HTMLButtonElement;

    // Act
    button.click();
    await settle();

    // Assert
    expect(cancel).toHaveBeenCalledWith('run1');
    expect([button.disabled, button.textContent?.trim()]).toEqual([true, 'Cancelling…']);
  });

  it('completed Succeeded navigates to the report', async () => {
    // Arrange
    const { settle } = await render();
    get = () => of(report);

    // Act
    completed.set('Succeeded');
    await settle();

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run1'], { replaceUrl: true });
  });

  it('completion_seenOnlyByPoll_navigatesToReport', async () => {
    // Arrange
    vi.useFakeTimers();
    const { fixture } = await render();
    get = () => of(report);

    // Act
    vi.advanceTimersByTime(5000);
    fixture.detectChanges();

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run1'], { replaceUrl: true });
  });

  it('failed run shows its reasons', async () => {
    // Arrange
    const { el, settle } = await render();
    get = () => of({ runId: 'run1', status: 'Failed', failureReasons: ['Unresolved citation: a.cs::B'] });

    // Act
    completed.set('Failed');
    await settle();

    // Assert
    expect(el.querySelector('.outcome')?.textContent).toContain('Unresolved citation: a.cs::B');
    expect(stop).toHaveBeenCalled();
  });

  it('a finished run on load redirects to the report', async () => {
    // Arrange
    get = () => of(report);

    // Act
    await render();

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run1'], { replaceUrl: true });
  });

  it('destroyed before the first read resolves never starts watching', async () => {
    // Arrange
    const first = new Subject<RunBody>();
    get = () => first;
    const watch = vi.fn();
    TestBed.configureTestingModule({
      imports: [LiveScan],
      providers: [
        provideTestAuth(),
        provideRouter([]),
        { provide: RunsApi, useValue: { get: () => get(), steps: () => of(steps), cancel, repository: () => of({ id: 'repo1' }) } },
        { provide: ScanProgressService, useValue: { watch } },
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(navigate);
    const fixture = TestBed.createComponent(LiveScan);
    fixture.componentRef.setInput('runId', 'run1');
    fixture.detectChanges();

    // Act
    fixture.destroy();
    first.next(running);

    // Assert
    expect(watch).not.toHaveBeenCalled();
  });

  it('a read in flight when leaving does not navigate', async () => {
    // Arrange
    const { fixture } = await render();
    const pending = new Subject<RunBody>();
    get = () => pending;
    completed.set('Succeeded');
    fixture.detectChanges();

    // Act
    fixture.destroy();
    pending.next(report);

    // Assert
    expect(navigate).not.toHaveBeenCalled();
  });

  it('stops the watch only once when finishing then leaving', async () => {
    // Arrange
    const { fixture, settle } = await render();
    get = () => of(report);
    completed.set('Succeeded');
    await settle();

    // Act
    fixture.destroy();

    // Assert
    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('poll re-reads the steps', async () => {
    // Arrange
    vi.useFakeTimers();
    const { fixture, el } = await render();
    steps = [step('P1', 'Succeeded')];

    // Act
    vi.advanceTimersByTime(5000);
    fixture.detectChanges();

    // Assert
    expect(state(el, 'P1')).toBe('done');
  });

  it('initial load error shows Run not found', async () => {
    // Arrange
    get = () => throwError(() => new HttpErrorResponse({ status: 404 }));

    // Act
    const { el } = await render();

    // Assert
    expect(el.querySelector('h1')?.textContent?.trim()).toBe('Run not found');
    expect(el.querySelector('button.cancel')).toBeNull();
  });

  it('succeeded without a report shows the report as missing', async () => {
    // Arrange
    const { el, settle } = await render();
    get = () => of({ runId: 'run1', status: 'Succeeded', failureReasons: [] });

    // Act
    completed.set('Succeeded');
    await settle();

    // Assert
    expect(el.querySelector('.outcome h2')?.textContent?.trim()).toBe('Scan finished, but its report is missing');
    expect(stop).toHaveBeenCalled();
  });

  it('stops watching on destroy', async () => {
    // Arrange
    const { fixture } = await render();

    // Act
    fixture.destroy();

    // Assert
    expect(stop).toHaveBeenCalled();
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';
import { RunsApi } from '../../core/api/runs-api';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { Comparison, RunHistoryItem } from '../../core/api/api-types';
import { RunDelta } from '../../core/api/report';
import { sampleReport } from '../../../testing/sample-report';
import { Compare } from './compare';

const PREVIOUS = '11111111-1111-4111-8111-111111111111';
const CURRENT = '22222222-2222-4222-8222-222222222222';

const comparison = (change: (c: Comparison) => void = () => undefined): Comparison => {
  const c: Comparison = {
    base: { runId: PREVIOUS, branch: 'main', commitSha: '0f1e2d3c4b5a69788796a5b4c3d2e1f001234567', generatedAt: '2026-11-01T09:00:00Z', score: 4.7 },
    target: { runId: CURRENT, branch: 'main', commitSha: 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678', generatedAt: '2026-11-15T09:00:00Z', score: 5.1 },
    swapped: false,
    adHoc: false,
    delta: sampleReport().delta as RunDelta,
  };
  change(c);
  return c;
};

const history = (runId: string, generatedAt: string): RunHistoryItem => ({
  runId, status: 'Succeeded', branch: 'main', commitSha: runId.replace(/-/g, ''), queuedAt: generatedAt, generatedAt,
  score: 5, weightAssessablePct: 90, endpointsTotal: 5, endpointsCovered: 3, endpointsWeak: 1, endpointsNone: 1, endpointsUnverified: 0, costUsd: 0, failureReasons: [],
});

describe('Compare', () => {
  let navigate: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<Compare>;

  async function render(
    result: Observable<Comparison>,
    params: { base?: string; target?: string } = { base: PREVIOUS, target: CURRENT },
    compareFor?: (base: string, target: string) => Observable<Comparison>,
  ) {
    TestBed.configureTestingModule({
      imports: [Compare],
      providers: [
        provideRouter([]),
        { provide: RunsApi, useValue: { compare: (b: string, t: string) => compareFor?.(b, t) ?? result, repository: () => of({ id: 'repo1', fullName: 'example-org/bookstore-api' }) } },
        { provide: RepositoriesApi, useValue: { runs: () => of([history(CURRENT, '2026-11-15T09:00:00Z'), history(PREVIOUS, '2026-11-01T09:00:00Z')]) } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(Compare);
    fixture.componentRef.setInput('base', params.base);
    fixture.componentRef.setInput('target', params.target);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('a slow answer for an earlier pair is ignored', async () => {
    // Arrange (the first pair answers late, after the user picked a new target)
    const slow = new Subject<Comparison>();
    const newer = comparison((c) => (c.delta.endpoints.newlyCovered = ['GET api/latest']));
    const el = await render(of(), { base: PREVIOUS, target: CURRENT }, (_b, t) => (t === CURRENT ? slow : of(newer)));
    fixture.componentRef.setInput('target', 'other');
    fixture.detectChanges();
    await fixture.whenStable();

    // Act
    slow.next(comparison());
    fixture.detectChanges();

    // Assert
    expect(panel(el, 'Newly covered').textContent).toContain('GET api/latest');
  });

  const panel = (el: HTMLElement, title: string) =>
    [...el.querySelectorAll('.panel')].find((p) => p.querySelector('h3')?.textContent?.trim() === title) as HTMLElement;

  it('example runs show newly covered POST api/orders and Regressed None.', async () => {
    // Act
    const el = await render(of(comparison()));

    // Assert
    expect(panel(el, 'Newly covered').textContent).toContain('POST api/orders');
    expect(panel(el, 'Regressed').textContent).toContain('None.');
  });

  it('like-for-like shows the ok banner', async () => {
    // Act
    const el = await render(of(comparison()));

    // Assert
    expect(el.querySelector('.banner.ok')?.textContent).toContain('These runs are like-for-like');
  });

  it('not comparable shows the warning banner and uncoloured changes', async () => {
    // Act
    const el = await render(of(comparison((c) => {
      c.delta.comparable = false;
      c.delta.comparability = ['integrationMappedTo changed'];
    })));

    // Assert
    expect(el.querySelector('.banner:not(.ok)')?.textContent).toContain('Not comparable: integrationMappedTo changed');
    expect(el.querySelectorAll('.metrics .up, .metrics .down').length).toBe(0);
  });

  it('metrics rows show labels and the change with direction', async () => {
    // Act
    const el = await render(of(comparison()));

    // Assert
    const first = el.querySelector('.metrics tbody tr') as HTMLElement;
    expect([...first.querySelectorAll('td')].map((td) => td.textContent?.trim())).toEqual(['Quality score', '4.7', '5.1', '▲ 0.4 improved']);
    expect(first.querySelector('td.up')).not.toBeNull();
  });

  it('ad-hoc label shown when adHoc', async () => {
    // Act
    const el = await render(of(comparison((c) => (c.adHoc = true))));

    // Assert
    expect(el.textContent).toContain('Ad-hoc comparison (not adjacent runs)');
  });

  it('swapped note shown when swapped', async () => {
    // Act
    const el = await render(of(comparison((c) => (c.swapped = true))));

    // Assert
    expect(el.textContent).toContain('Swapped to compare older → newer');
  });

  it('changing the target picker navigates with both query params', async () => {
    // Arrange
    const el = await render(of(comparison()));
    const select = el.querySelector('select[name=target]') as HTMLSelectElement;

    // Act
    select.value = PREVIOUS;
    select.dispatchEvent(new Event('change'));

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/compare'], { queryParams: { base: PREVIOUS, target: PREVIOUS } });
  });

  it('a base run missing from the picker list shows the placeholder, not the first run', async () => {
    // Act
    const el = await render(of(comparison()), { base: 'not-listed', target: CURRENT });

    // Assert
    const select = el.querySelector('select[name=base]') as HTMLSelectElement;
    expect(select.selectedOptions[0].textContent?.trim()).toBe('Pick a run');
  });

  it('compare error shows cannot be compared', async () => {
    // Act
    const el = await render(throwError(() => new HttpErrorResponse({ status: 409 })));

    // Assert
    expect(el.textContent).toContain("These runs can't be compared.");
  });

  it('compare error shows the API reason', async () => {
    // Act
    const el = await render(throwError(() => new HttpErrorResponse({ status: 400, error: { detail: "Runs from different repositories can't be compared." } })));

    // Assert
    expect(el.textContent).toContain("These runs can't be compared: Runs from different repositories can't be compared.");
  });

  it('missing params asks to pick two runs', async () => {
    // Act
    const el = await render(of(comparison()), {});

    // Assert
    expect(el.textContent).toContain('Pick two runs to compare.');
  });
});

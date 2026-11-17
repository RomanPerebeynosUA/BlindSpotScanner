import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';
import { RunsApi } from '../../core/api/runs-api';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { RemediationApi } from '../../core/api/remediation-api';
import { RemediationAction, RunBody, RunHistoryItem } from '../../core/api/api-types';
import { IntegrationReport } from '../../core/api/report';
import { sampleReport } from '../../../testing/sample-report';
import { Report } from './report';
import { ReportStore } from './report-store';

const PREVIOUS = '11111111-1111-4111-8111-111111111111';

const historyItem = (runId: string, generatedAt: string, status: RunHistoryItem['status'] = 'Succeeded'): RunHistoryItem => ({
  runId, status, branch: 'main', commitSha: runId.replace(/-/g, '').padEnd(40, '0'), queuedAt: generatedAt, generatedAt,
  score: 5, weightAssessablePct: 90, endpointsTotal: 5, endpointsCovered: 3, endpointsWeak: 1, endpointsNone: 1, endpointsUnverified: 0, costUsd: 0, failureReasons: [],
});

/** The repository's history, newest first: this run, a failed one, then the previous example run. */
const defaultHistory = [
  historyItem('run1', '2026-11-15T09:00:00Z'),
  historyItem('failed', '2026-11-10T09:00:00Z', 'Failed'),
  historyItem(PREVIOUS, '2026-11-01T09:00:00Z'),
];

describe('Report', () => {
  let navigate: ReturnType<typeof vi.fn>;
  let get: ReturnType<typeof vi.fn>;

  async function render(
    body: Observable<RunBody>,
    history: RunHistoryItem[] = defaultHistory,
    getFor?: (runId: string) => Observable<RunBody>,
    actions: RemediationAction[] | Observable<RemediationAction[]> = [],
  ) {
    get = vi.fn((runId: string) => getFor?.(runId) ?? body);
    TestBed.configureTestingModule({
      imports: [Report],
      providers: [
        provideRouter([]),
        { provide: RunsApi, useValue: { get, repository: () => of({ id: 'repo1' }) } },
        { provide: RepositoriesApi, useValue: { runs: () => of(history) } },
        { provide: RemediationApi, useValue: { actions: () => (Array.isArray(actions) ? of(actions) : actions), features: () => of({ pullRequests: true, jira: true }) } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as unknown as ReturnType<typeof vi.fn>;
    const fixture = TestBed.createComponent(Report);
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
    return { el, settle, fixture };
  }

  const withReport = (change: (r: IntegrationReport) => void = () => undefined) => {
    const report = sampleReport();
    change(report);
    return of(report as RunBody);
  };

  const kpi = (el: HTMLElement, label: string) =>
    [...el.querySelectorAll('.kpi')].find((k) => k.querySelector('.lbl')?.textContent?.trim() === label) as HTMLElement;

  const click = (el: HTMLElement, selector: string) => (el.querySelector(selector) as HTMLButtonElement).click();

  it('kpis come from the summary', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(kpi(el, 'Quality score').querySelector('.val')?.textContent?.replace(/\s+/g, '')).toBe('5.1/10');
    expect(kpi(el, 'Quality score').querySelector('.sub')?.textContent?.trim()).toBe('90% of weight assessable');
    expect(kpi(el, 'Endpoints covered').querySelector('.val')?.textContent?.replace(/\s+/g, '')).toBe('3/5');
    expect(kpi(el, 'Untested').querySelector('.val')?.textContent?.trim()).toBe('1');
  });

  it('header shows repo, short sha link and the measurement label', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(el.querySelector('h1')?.textContent?.trim()).toBe('example-org/bookstore-api');
    const sha = el.querySelector('a.sha') as HTMLAnchorElement;
    expect([sha.textContent?.trim(), sha.getAttribute('href')]).toEqual([
      'a1b2c3d',
      'https://github.com/example-org/bookstore-api/commit/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678',
    ]);
    expect(el.querySelector('.mapping')?.textContent?.trim()).toBe('real-seam');
  });

  it('scoreTile_nullScore_showsNA', async () => {
    // Act
    const { el } = await render(withReport((r) => {
      r.summary.qualityScore = null;
      r.summary.weightAssessablePct = 0;
    }));

    // Assert (principle 5: the score always carries "% of weight assessable")
    expect(kpi(el, 'Quality score').querySelector('.val')?.textContent?.trim()).toBe('N/A');
    expect(kpi(el, 'Quality score').querySelector('.sub')?.textContent?.trim()).toBe('Not scorable · 0% of weight assessable');
  });

  it('summary shows the four computed bullets', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect([...el.querySelectorAll('.bullets li b')].map((b) => b.textContent?.trim())).toEqual(['Overall', 'Coverage', 'Test quality', 'Since last scan']);
  });

  it('coverage_filterHidesAll_showsEmptyRow', async () => {
    // Arrange
    const { el, settle } = await render(withReport());

    // Act
    click(el, '.filters button[data-status="skipped"]');
    await settle();

    // Assert
    expect(el.querySelector('.coverage tbody')?.textContent?.trim()).toBe('No endpoints with this status.');
  });

  it('filter buttons show counts and pressed state', async () => {
    // Arrange
    const { el, settle } = await render(withReport());

    // Act
    click(el, '.filters button[data-status="weak"]');
    await settle();

    // Assert
    const weak = el.querySelector('.filters button[data-status="weak"]') as HTMLButtonElement;
    expect([weak.textContent?.trim(), weak.getAttribute('aria-pressed')]).toEqual(['⚠️ Weak 1', 'true']);
    expect(el.querySelectorAll('.coverage tr.endpoint').length).toBe(1);
  });

  it('row expansion shows evidence links and notes', async () => {
    // Arrange
    const { el, settle } = await render(withReport());
    const row = el.querySelector('.coverage tr.endpoint[data-key="GET api/books/{id}"]') as HTMLElement;

    // Act
    (row.querySelector('button.expand') as HTMLButtonElement).click();
    await settle();

    // Assert
    const detail = el.querySelector('.coverage tr.detail') as HTMLElement;
    expect(detail.textContent).toContain('Asserts only 200 OK.');
    expect(detail.textContent).toContain('False positive');
    expect(detail.querySelector('a.ev')?.getAttribute('href')).toBe(
      'https://github.com/example-org/bookstore-api/blob/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678/tests/Bookstore.IntegrationTests/BooksApiTests.cs',
    );
  });

  it('scorecard unscorable row shows N/A', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    const row = el.querySelector('.scorecard tr[data-id="6"]') as HTMLElement;
    expect(row.querySelector('.chip')?.textContent?.trim()).toBe('N/A');
  });

  it('findings empty group shows None', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(el.querySelector('.findings [data-kind="SkippedTest"]')?.textContent).toContain('None.');
    expect(el.querySelector('.findings [data-kind="FalsePositive"]')?.textContent).toContain('A book lookup test only checks that the request succeeded');
  });

  it('top risk detail hidden until expanded', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    const risks = [...el.querySelectorAll('.risks details')] as HTMLDetailsElement[];
    expect(risks.map((d) => d.open)).toEqual([true, ...risks.slice(1).map(() => false)]);
    expect(risks[1].querySelector('summary')?.textContent).toBeTruthy();
  });

  it('running run redirects to live', async () => {
    // Act
    await render(of({ runId: 'run1', status: 'Running', failureReasons: [] }));

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run1', 'live'], { replaceUrl: true });
  });

  it('failed run shows reasons', async () => {
    // Act
    const { el } = await render(of({ runId: 'run1', status: 'Failed', failureReasons: ['Unresolved citation: a.cs::B'] }));

    // Assert
    expect(el.querySelector('.outcome')?.textContent).toContain('Unresolved citation: a.cs::B');
  });

  it('succeeded run without a report says the report is missing', async () => {
    // Act
    const { el } = await render(of({ runId: 'run1', status: 'Succeeded', failureReasons: [] }));

    // Assert
    expect(el.querySelector('.outcome h1')?.textContent?.trim()).toBe('Scan finished, but its report is missing');
  });

  it('expanding one of two identical endpoints opens only that one', async () => {
    // Arrange (D14: the same route can be declared twice)
    const { el, settle } = await render(withReport((r) => {
      const books = r.endpointCoverage[0];
      books.endpoints = [books.endpoints[0], { ...books.endpoints[0] }];
    }));

    // Act
    (el.querySelector('.coverage tr.endpoint button.expand') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(el.querySelectorAll('.coverage tr.detail').length).toBe(1);
  });

  it('score tile shows the delta chip ▲ 0.4', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    const chip = kpi(el, 'Quality score').querySelector('.delta') as HTMLElement;
    expect([chip.textContent?.trim(), chip.classList.contains('up')]).toEqual(['▲ 0.4', true]);
  });

  it('not comparable shows the banner and flat chips', async () => {
    // Act
    const { el } = await render(withReport((r) => {
      r.delta!.comparable = false;
      r.delta!.comparability = ['integrationMappedTo changed'];
    }));

    // Assert
    expect(el.querySelector('.banner')?.textContent).toContain('Not comparable with the previous scan: integrationMappedTo changed');
    expect(el.querySelectorAll('.kpi .delta.up, .kpi .delta.down').length).toBe(0);
  });

  it('first scan shows no banner and no changes', async () => {
    // Act
    const { el } = await render(withReport((r) => (r.delta = null)));

    // Assert
    expect([el.querySelector('.banner'), el.querySelector('app-delta-changes')]).toEqual([null, null]);
  });

  it('changes section names the base date', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(el.querySelector('.changes-since h2')?.textContent?.trim()).toBe('Changes since 1 Nov 2026');
  });

  it('run picker lists succeeded runs newest first', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    const options = [...el.querySelectorAll('select.run-picker option')].map((o) => (o as HTMLOptionElement).value);
    expect(options).toEqual(['run1', PREVIOUS]);
  });

  it('older button navigates to the previous run', async () => {
    // Arrange
    const { el } = await render(withReport());

    // Act
    (el.querySelector('button.older') as HTMLButtonElement).click();

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/runs', PREVIOUS]);
  });

  it('newest run disables the newer button', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect((el.querySelector('button.newer') as HTMLButtonElement).disabled).toBe(true);
  });

  it('Compare with links base and target', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(el.querySelector('a.compare')?.getAttribute('href')).toBe(`/compare?base=${PREVIOUS}&target=run1`);
  });

  it('navigating to another run reloads the report', async () => {
    // Arrange
    const { fixture, settle } = await render(withReport());

    // Act
    fixture.componentRef.setInput('runId', PREVIOUS);
    await settle();

    // Assert
    expect(get).toHaveBeenLastCalledWith(PREVIOUS);
  });

  it('a slow answer for the run the user left is ignored', async () => {
    // Arrange (run1 answers late, after the user moved on to the previous run)
    const slow = new Subject<RunBody>();
    const previous = sampleReport();
    previous.meta.repo.name = 'previous-run';
    const { fixture, el, settle } = await render(of(), defaultHistory, (id) => (id === 'run1' ? slow : of(previous as RunBody)));
    fixture.componentRef.setInput('runId', PREVIOUS);
    await settle();

    // Act
    slow.next({ runId: 'run1', status: 'Running', failureReasons: [] });
    await settle();

    // Assert
    expect(el.querySelector('h1')?.textContent?.trim()).toBe('example-org/previous-run');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('unknown run shows Run not found', async () => {
    // Act
    const { el } = await render(throwError(() => new HttpErrorResponse({ status: 404 })));

    // Assert
    expect(el.textContent).toContain('Run not found');
  });

  const endpointRow = (el: HTMLElement, key: string) => el.querySelector(`tr.endpoint[data-key="${key}"]`) as HTMLElement;

  it('untested endpoint row shows Create Jira', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(endpointRow(el, 'POST api/orders/{id}/cancel').querySelector('app-jira-action button')?.textContent?.trim()).toBe('Create Jira');
  });

  it('covered endpoint row shows no Jira button', async () => {
    // Act
    const { el } = await render(withReport());

    // Assert
    expect(endpointRow(el, 'GET api/books').querySelector('app-jira-action')).toBeNull();
  });

  it('actions log lists the repository tickets', async () => {
    // Arrange
    const ticket: RemediationAction = {
      id: 'a1', type: 'JiraTicket', stableKey: 'uncovered-seam:payment-gateway', externalKey: 'BSS-4',
      url: 'https://example.atlassian.net/browse/BSS-4', status: 'Done', createdAt: '2026-11-17T10:00:00Z',
    };

    // Act
    const { el } = await render(withReport(), defaultHistory, undefined, [ticket]);

    // Assert
    expect(el.querySelector('app-actions-log')?.textContent).toContain('BSS-4');
    expect(el.querySelector('[data-kind=UncoveredSeam] app-jira-action a')?.textContent?.trim()).toBe('BSS-4 · Done');
  });

  it('a slow actions load keeps a ticket created meanwhile', async () => {
    // Arrange (the load is slow while the server refreshes statuses from Jira)
    const slow = new Subject<RemediationAction[]>();
    const { fixture, settle, el } = await render(withReport(), defaultHistory, undefined, slow);
    const created: RemediationAction = {
      id: 'new', type: 'JiraTicket', stableKey: 'uncovered-seam:payment-gateway', externalKey: 'BSS-5',
      url: 'https://example.atlassian.net/browse/BSS-5', status: 'To Do', createdAt: '2026-11-17T11:00:00Z',
    };
    fixture.debugElement.injector.get(ReportStore).actions.update((a) => [created, ...a]);

    // Act
    slow.next([]);
    slow.complete();
    await settle();

    // Assert
    expect(el.querySelector('[data-kind=UncoveredSeam] app-jira-action a')?.textContent?.trim()).toBe('BSS-5 · To Do');
  });
});

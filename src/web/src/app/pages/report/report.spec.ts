import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { RunsApi } from '../../core/api/runs-api';
import { RunBody } from '../../core/api/api-types';
import { IntegrationReport } from '../../core/api/report';
import { sampleReport } from '../../../testing/sample-report';
import { Report } from './report';

describe('Report', () => {
  const navigate = vi.fn().mockResolvedValue(true);

  beforeEach(() => navigate.mockClear());

  async function render(body: Observable<RunBody>) {
    TestBed.configureTestingModule({
      imports: [Report],
      providers: [
        { provide: Router, useValue: { navigate } },
        { provide: RunsApi, useValue: { get: () => body } },
      ],
    });
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
    return { el, settle };
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
    const { el } = await render(withReport((r) => (r.summary.qualityScore = null)));

    // Assert
    expect(kpi(el, 'Quality score').querySelector('.val')?.textContent?.trim()).toBe('N/A');
    expect(kpi(el, 'Quality score').querySelector('.sub')?.textContent?.trim()).toBe('Not scorable');
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

  it('unknown run shows Run not found', async () => {
    // Act
    const { el } = await render(throwError(() => new HttpErrorResponse({ status: 404 })));

    // Assert
    expect(el.textContent).toContain('Run not found');
  });
});

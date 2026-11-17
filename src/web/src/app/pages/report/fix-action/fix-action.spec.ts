import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { RemediationAction } from '../../../core/api/api-types';
import { Finding } from '../../../core/api/report';
import { sampleReport } from '../../../../testing/sample-report';
import { ReportStore } from '../report-store';
import { FixDialog } from '../fix-dialog/fix-dialog';
import { FixAction } from './fix-action';

const pr = (stableKey: string): RemediationAction => ({
  id: 'p1', type: 'PullRequest', stableKey, externalKey: '#7', url: 'https://github.com/example-org/bookstore-api/pull/7', status: 'Open · checks passing', createdAt: '2026-11-17T10:00:00Z',
});

describe('FixAction', () => {
  let open: ReturnType<typeof vi.fn>;

  function render(finding: Finding, pullRequests = true, actions: RemediationAction[] = []) {
    open = vi.fn(() => ({ afterClosed: () => of(undefined) }));
    TestBed.configureTestingModule({ imports: [FixAction], providers: [ReportStore, { provide: MatDialog, useValue: { open } }] });
    const store = TestBed.inject(ReportStore);
    store.report.set(sampleReport());
    store.features.set({ pullRequests, jira: true });
    store.actions.set(actions);
    const fixture = TestBed.createComponent(FixAction);
    fixture.componentRef.setInput('finding', finding);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const finding = (id: string) => sampleReport().findings.find((f) => f.id === id)!;

  it('read-only hides the Fix with PR button', () => {
    // Act
    const el = render(finding('f3'), false);

    // Assert
    expect(el.querySelector('button')).toBeNull();
  });

  it('fixable finding shows Fix with PR', () => {
    // Act
    const el = render(finding('f3'));

    // Assert
    const button = el.querySelector('button') as HTMLButtonElement;
    expect([button.textContent?.trim(), button.getAttribute('aria-label')]).toEqual(['Fix with PR', `Fix with PR: ${finding('f3').headline}`]);
  });

  it('uncovered seam shows no Fix button', () => {
    // Act
    const el = render(finding('f4'));

    // Assert
    expect(el.querySelector('button')).toBeNull();
  });

  it('existing PR shows its link', () => {
    // Act
    const el = render(finding('f3'), true, [pr(finding('f3').stableKey)]);

    // Assert
    expect(el.querySelector('a')?.textContent?.trim()).toBe('#7 · Open · checks passing');
  });

  it('click opens the fix dialog for the run and finding', () => {
    // Arrange
    const el = render(finding('f3'));

    // Act
    (el.querySelector('button') as HTMLButtonElement).click();

    // Assert
    expect(open).toHaveBeenCalledWith(FixDialog, expect.objectContaining({ data: { runId: sampleReport().meta.runId, findingId: 'f3' }, ariaLabelledBy: 'fix-title' }));
  });
});

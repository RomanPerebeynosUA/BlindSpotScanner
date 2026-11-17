import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { RemediationAction } from '../../../core/api/api-types';
import { Finding } from '../../../core/api/report';
import { sampleReport } from '../../../../testing/sample-report';
import { ReportStore } from '../report-store';
import { JiraDialog } from '../jira-dialog/jira-dialog';
import { JiraAction } from './jira-action';

const ticket = (stableKey: string, key = 'BSS-1'): RemediationAction => ({
  id: key, type: 'JiraTicket', stableKey, externalKey: key, url: `https://example.atlassian.net/browse/${key}`, status: 'In Progress', createdAt: '2026-11-17T10:00:00Z',
});

describe('JiraAction', () => {
  let open: ReturnType<typeof vi.fn>;
  let store: ReportStore;

  function render(finding: Finding, actions: RemediationAction[] = [], created?: RemediationAction) {
    open = vi.fn(() => ({ afterClosed: () => of(created) }));
    TestBed.configureTestingModule({ imports: [JiraAction], providers: [ReportStore, { provide: MatDialog, useValue: { open } }] });
    store = TestBed.inject(ReportStore);
    store.report.set(sampleReport());
    store.actions.set(actions);
    const fixture = TestBed.createComponent(JiraAction);
    fixture.componentRef.setInput('finding', finding);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, fixture };
  }

  const f3 = () => sampleReport().findings.find((f) => f.id === 'f3')!;

  it('finding without ticket shows Create Jira', () => {
    // Act
    const { el } = render(f3());

    // Assert
    expect(el.querySelector('button')?.textContent?.trim()).toBe('Create Jira');
  });

  it('finding with ticket shows key and status link', () => {
    // Act
    const { el } = render(f3(), [ticket(f3().stableKey)]);

    // Assert
    const link = el.querySelector('a') as HTMLAnchorElement;
    expect([link.textContent?.trim(), link.href, el.querySelector('button')]).toEqual(['BSS-1 · In Progress', 'https://example.atlassian.net/browse/BSS-1', null]);
  });

  it('create opens the dialog for the run and finding and shows the new ticket', () => {
    // Arrange
    const { el, fixture } = render(f3(), [], ticket(f3().stableKey, 'BSS-9'));

    // Act
    (el.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(open).toHaveBeenCalledWith(JiraDialog, expect.objectContaining({ data: { runId: sampleReport().meta.runId, findingId: 'f3' } }));
    expect(el.querySelector('a')?.textContent?.trim()).toBe('BSS-9 · In Progress');
  });
});

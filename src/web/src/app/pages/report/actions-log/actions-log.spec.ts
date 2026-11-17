import { TestBed } from '@angular/core/testing';
import { RemediationAction } from '../../../core/api/api-types';
import { ActionsLog } from './actions-log';

const ticket: RemediationAction = {
  id: 'a1', type: 'JiraTicket', stableKey: 'uncovered-seam:payment-gateway', externalKey: 'BSS-1',
  url: 'https://example.atlassian.net/browse/BSS-1', status: 'Done', createdAt: '2026-11-17T10:00:00Z',
};

describe('ActionsLog', () => {
  function render(actions: RemediationAction[]): HTMLElement {
    const fixture = TestBed.createComponent(ActionsLog);
    fixture.componentRef.setInput('actions', actions);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists tickets with status', () => {
    // Act
    const el = render([ticket]);

    // Assert
    const cells = [...el.querySelectorAll('tbody td')].map((td) => td.textContent?.trim());
    expect(cells).toEqual(['Jira', 'BSS-1', 'Done', 'uncovered-seam:payment-gateway', '17 Nov 2026']);
  });

  it('empty shows No tickets yet.', () => {
    // Act
    const el = render([]);

    // Assert
    expect(el.textContent).toContain('No tickets yet.');
  });
});

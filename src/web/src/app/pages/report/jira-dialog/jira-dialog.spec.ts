import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { RemediationApi } from '../../../core/api/remediation-api';
import { JiraCreate, JiraDraft, RemediationAction } from '../../../core/api/api-types';
import { JiraDialog } from './jira-dialog';

const ticket = (key = 'BSS-1'): RemediationAction => ({
  id: 'a1', type: 'JiraTicket', stableKey: 'uncovered-endpoint:POST api/orders/{}/cancel', externalKey: key,
  url: `https://example.atlassian.net/browse/${key}`, status: 'To Do', createdAt: '2026-11-17T10:00:00Z', createdBy: 'test-user',
});

const draft = (existing: RemediationAction | null = null): JiraDraft => ({
  summary: '[Integration coverage] Add test for POST api/orders/{id}/cancel',
  description: '## Problem\nCancelling an order has no integration test',
  projectKey: 'BSS', issueType: 'Task', priority: 'High', labels: ['integration-coverage'],
  stableKey: 'uncovered-endpoint:POST api/orders/{}/cancel', existing,
});

describe('JiraDialog', () => {
  let close: ReturnType<typeof vi.fn>;
  let createJira: ReturnType<typeof vi.fn>;

  async function render(preview: JiraDraft | Observable<JiraDraft>, create: () => Observable<RemediationAction> = () => of(ticket('BSS-2'))) {
    close = vi.fn();
    createJira = vi.fn(create);
    TestBed.configureTestingModule({
      imports: [JiraDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { runId: 'run1', findingId: 'f2' } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: RemediationApi, useValue: { jiraPreview: () => ('summary' in preview ? of(preview) : preview), createJira } },
      ],
    });
    const fixture = TestBed.createComponent(JiraDialog);
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

  const field = <T extends HTMLElement>(el: HTMLElement, name: string) => el.querySelector(`[name=${name}]`) as T;

  const type = (input: HTMLInputElement | HTMLTextAreaElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submit = (el: HTMLElement) => (el.querySelector('button[type=submit]') as HTMLButtonElement).click();

  it('prefills the draft', async () => {
    // Act
    const { el } = await render(draft());

    // Assert
    expect([
      field<HTMLInputElement>(el, 'summary').value,
      field<HTMLTextAreaElement>(el, 'description').value,
      field<HTMLSelectElement>(el, 'priority').value,
      field<HTMLInputElement>(el, 'labels').value,
      el.querySelector('.target')?.textContent?.replace(/\s+/g, ' ').trim(),
    ]).toEqual([
      '[Integration coverage] Add test for POST api/orders/{id}/cancel',
      '## Problem\nCancelling an order has no integration test',
      'High',
      'integration-coverage',
      'Project BSS · Task',
    ]);
  });

  it('submit sends the edited fields and closes with the ticket', async () => {
    // Arrange
    const { el, settle } = await render(draft());
    type(field(el, 'summary'), 'Edited summary');
    type(field(el, 'labels'), 'integration-coverage, qa');

    // Act
    submit(el);
    await settle();

    // Assert
    const body: JiraCreate = {
      summary: 'Edited summary', description: '## Problem\nCancelling an order has no integration test', projectKey: 'BSS', issueType: 'Task',
      priority: 'High', labels: ['integration-coverage', 'qa'], force: false,
    };
    expect(createJira).toHaveBeenCalledWith('run1', 'f2', body);
    expect(close).toHaveBeenCalledWith(ticket('BSS-2'));
  });

  it('existing ticket shows the warning and Create anyway sends force', async () => {
    // Arrange
    const { el, settle } = await render(draft(ticket()));
    const banner = el.querySelector('.banner')?.textContent;
    const label = el.querySelector('button[type=submit]')?.textContent?.trim();

    // Act
    submit(el);
    await settle();

    // Assert
    expect(banner).toContain('Already ticketed: BSS-1 (To Do)');
    expect(label).toBe('Create anyway');
    expect(createJira.mock.calls[0][2].force).toBe(true);
  });

  it('409 switches to the existing-ticket state', async () => {
    // Arrange
    const conflict = new HttpErrorResponse({ status: 409, error: { title: 'Ticket already exists', existing: ticket('BSS-7') } });
    const { el, settle } = await render(draft(), () => throwError(() => conflict));

    // Act
    submit(el);
    await settle();

    // Assert
    expect([el.querySelector('.banner')?.textContent?.includes('Already ticketed: BSS-7'), close.mock.calls.length]).toEqual([true, 0]);
  });

  it('loading text is announced', async () => {
    // Act
    const { el } = await render(NEVER);

    // Assert
    expect(el.querySelector('[aria-live=polite]')?.textContent?.trim()).toBe('Preparing the draft…');
  });
});

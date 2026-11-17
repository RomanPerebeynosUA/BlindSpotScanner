import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { RemediationApi } from '../../../core/api/remediation-api';
import { FixCreate, FixDraft, RemediationAction } from '../../../core/api/api-types';
import { FixDialog } from './fix-dialog';

const created: RemediationAction = {
  id: 'p1', type: 'PullRequest', stableKey: 'uncovered-endpoint:POST api/orders/{}/cancel', externalKey: '#7',
  url: 'https://github.com/example-org/bookstore-api/pull/7', status: 'Open', createdAt: '2026-11-17T10:00:00Z',
};

const draft = (): FixDraft => ({
  files: [{ path: 'tests/Bookstore.IntegrationTests/CancelOrderTests.cs', action: 'create', content: 'public class CancelOrderTests\n{\n}', original: null }],
  rationale: 'Covers cancelling an order.',
  assumptions: ['Orders are seeded through DbFixture.'],
  riskNotes: [],
  branch: 'coverage/fix-uncovered-endpoint-post-api-orders-cancel-a1b2c3d',
  title: 'test: add integration test for POST api/orders/{id}/cancel',
  body: 'Adds a test.',
  costUsd: 0.04,
  existing: null,
  baseSha: 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678',
});

describe('FixDialog', () => {
  let close: ReturnType<typeof vi.fn>;
  let fixPreview: ReturnType<typeof vi.fn>;
  let createPullRequest: ReturnType<typeof vi.fn>;

  async function render(preview: () => Observable<FixDraft>, create: () => Observable<RemediationAction> = () => of(created)) {
    close = vi.fn();
    fixPreview = vi.fn(preview);
    createPullRequest = vi.fn(create);
    TestBed.configureTestingModule({
      imports: [FixDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { runId: 'run1', findingId: 'f2' } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: RemediationApi, useValue: { fixPreview, createPullRequest } },
      ],
    });
    const fixture = TestBed.createComponent(FixDialog);
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

  const type = (el: HTMLElement, name: string, value: string) => {
    const input = el.querySelector(`[name="${name}"]`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submit = (el: HTMLElement) => (el.querySelector('button[type=submit]') as HTMLButtonElement).click();

  const problem = (status: number, title: string, extra: Record<string, unknown>) => new HttpErrorResponse({ status, error: { title, ...extra } });

  it('shows the billed generating note', async () => {
    // Act
    const { el } = await render(() => NEVER);

    // Assert
    expect(el.querySelector('[aria-live=polite]')?.textContent?.trim()).toBe('Generating… (this calls Claude and is billed)');
  });

  it('renders the diff with added lines', async () => {
    // Act
    const { el } = await render(() => of(draft()));

    // Assert
    const added = [...el.querySelectorAll('.diff .add')].map((l) => l.textContent);
    expect([added, el.querySelector('.cost')?.textContent?.trim()]).toEqual([['+ public class CancelOrderTests', '+ {', '+ }'], 'Cost: $0.04']);
  });

  it('the diff is a region named after its file', async () => {
    // Act
    const { el } = await render(() => of(draft()));

    // Assert
    expect(el.querySelector('[role=region][aria-label="Changes to tests/Bookstore.IntegrationTests/CancelOrderTests.cs"]')).not.toBeNull();
  });

  it('a failed open without reasons shows the API detail inline', async () => {
    // Arrange
    const exists = problem(409, 'Branch exists', { detail: 'Branch coverage/fix-x already exists; rename it.' });
    const { el, settle } = await render(() => of(draft()), () => throwError(() => exists));

    // Act
    submit(el);
    await settle();

    // Assert
    expect(el.querySelector('.error')?.textContent?.trim()).toBe('Branch coverage/fix-x already exists; rename it.');
  });

  it('submit sends edited branch, title and files', async () => {
    // Arrange
    const { el, settle } = await render(() => of(draft()));
    type(el, 'branch', 'coverage/fix-cancel');
    type(el, 'title', 'test: cancel an order');
    (el.querySelector('button.edit') as HTMLButtonElement).click();
    await settle();
    const editor = el.querySelector('textarea[name="file-0"]') as HTMLTextAreaElement;
    editor.value = 'public class Edited { }';
    editor.dispatchEvent(new Event('input'));

    // Act
    submit(el);
    await settle();

    // Assert
    const body: FixCreate = {
      files: [{ path: 'tests/Bookstore.IntegrationTests/CancelOrderTests.cs', action: 'create', content: 'public class Edited { }' }],
      branch: 'coverage/fix-cancel', title: 'test: cancel an order', body: 'Adds a test.', force: false, baseSha: 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678',
    };
    expect(createPullRequest).toHaveBeenCalledWith('run1', 'f2', body);
    expect(close).toHaveBeenCalledWith(created);
  });

  it('files changed shows the paths and Regenerate re-runs the preview', async () => {
    // Arrange
    const changed = problem(409, 'Files changed since the scan', { paths: ['tests/Bookstore.IntegrationTests/BooksApiTests.cs'] });
    const { el, settle } = await render(() => of(draft()), () => throwError(() => changed));
    submit(el);
    await settle();
    const banner = el.querySelector('.banner')?.textContent;

    // Act
    (el.querySelector('button.regenerate') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect([banner?.includes('tests/Bookstore.IntegrationTests/BooksApiTests.cs'), fixPreview.mock.calls[1]]).toEqual([true, ['run1', 'f2', true]]);
  });

  it('rejected shows the reasons', async () => {
    // Arrange
    const rejected = problem(422, 'Fix rejected', { reasons: ['src/X.cs: outside the test project tests/Bookstore.IntegrationTests'] });
    const { el, settle } = await render(() => of(draft()), () => throwError(() => rejected));

    // Act
    submit(el);
    await settle();

    // Assert
    expect([el.querySelector('.reasons')?.textContent?.includes('src/X.cs: outside the test project'), close.mock.calls.length]).toEqual([true, 0]);
  });

  it('declined shows the rationale and no Open button', async () => {
    // Act
    const declined = problem(422, 'Declined', { detail: 'The skip reason is still valid.', reasons: ['The skip reason is still valid.'] });
    const { el } = await render(() => throwError(() => declined));

    // Assert
    expect([el.textContent?.includes('The skip reason is still valid.'), el.querySelector('button[type=submit]')]).toEqual([true, null]);
  });
});

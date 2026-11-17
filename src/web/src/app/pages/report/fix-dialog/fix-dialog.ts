import { Component, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { RemediationApi } from '../../../core/api/remediation-api';
import { FixDraft, FixDraftFile, RemediationAction } from '../../../core/api/api-types';
import { DiffLine, lineDiff } from '../../../shared/line-diff/line-diff';

export interface FixDialogData {
  runId: string;
  findingId: string;
}

interface EditableFile extends FixDraftFile {
  editing: boolean;
}

interface Problem {
  title?: string;
  detail?: string;
  reasons?: string[];
  paths?: string[];
  existing?: RemediationAction;
}

/**
 * Doc 04 Fix-with-PR dialog (D25): generating (billed), then the diff with editable files, branch,
 * title and body, then the draft PR on the user's click. Files changed since the scan offer
 * "Regenerate"; guard rejections are listed; a declined fix shows why. Closes with the PR, or nothing.
 */
@Component({
  selector: 'app-fix-dialog',
  imports: [DecimalPipe],
  templateUrl: './fix-dialog.html',
  styleUrl: './fix-dialog.scss',
})
export class FixDialog {
  private readonly api = inject(RemediationApi);
  private readonly dialog = inject<MatDialogRef<FixDialog, RemediationAction>>(MatDialogRef);
  private readonly data = inject<FixDialogData>(MAT_DIALOG_DATA);

  protected readonly draft = signal<FixDraft | null>(null);
  protected readonly files = signal<EditableFile[]>([]);
  protected readonly branch = signal('');
  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly existing = signal<RemediationAction | null>(null);
  protected readonly declined = signal<string | null>(null);
  protected readonly reasons = signal<string[]>([]);
  protected readonly changed = signal<string[]>([]);
  protected readonly busy = signal(false);
  /** Why opening failed when the API gave no list (branch exists, GitHub error). */
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.generate();
  }

  /** `regenerate` builds against the default branch head, after "files changed" (D25). */
  protected generate(regenerate = false): void {
    // Nothing from the previous draft may linger while the new one is generated.
    this.draft.set(null);
    this.changed.set([]);
    this.reasons.set([]);
    this.declined.set(null);
    this.existing.set(null);
    this.error.set(null);
    this.busy.set(false);
    this.api.fixPreview(this.data.runId, this.data.findingId, regenerate).subscribe({
      next: (draft) => {
        this.draft.set(draft);
        this.files.set(draft.files.map((f) => ({ ...f, editing: false })));
        this.branch.set(draft.branch);
        this.title.set(draft.title);
        this.body.set(draft.body);
        this.existing.set(draft.existing);
      },
      error: (error: unknown) => {
        const problem = this.problem(error);
        if (problem?.title === 'Declined') {
          this.declined.set(problem.detail ?? 'The fix was declined.');
        } else if (problem?.reasons?.length) {
          this.reasons.set(problem.reasons);
        } else {
          // The interceptor shows why (for example read-only mode); there is nothing to review.
          this.dialog.close();
        }
      },
    });
  }

  protected diff(file: FixDraftFile): DiffLine[] {
    return lineDiff(file.original ?? '', file.content);
  }

  protected toggle(index: number): void {
    this.files.update((files) => files.map((f, i) => (i === index ? { ...f, editing: !f.editing } : f)));
  }

  protected edit(index: number, event: Event): void {
    const content = this.read(event);
    this.files.update((files) => files.map((f, i) => (i === index ? { ...f, content } : f)));
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.draft() || this.busy() || !this.branch().trim() || !this.title().trim()) {
      return;
    }

    this.busy.set(true);
    this.reasons.set([]);
    this.error.set(null);
    const body = {
      files: this.files().map((f) => ({ path: f.path, action: f.action, content: f.content })),
      branch: this.branch().trim(),
      title: this.title().trim(),
      body: this.body(),
      force: this.existing() !== null,
      baseSha: this.draft()!.baseSha,
    };
    this.api.createPullRequest(this.data.runId, this.data.findingId, body).subscribe({
      next: (action) => this.dialog.close(action),
      error: (error: unknown) => {
        this.busy.set(false);
        const problem = this.problem(error);
        if (problem?.paths) {
          this.changed.set(problem.paths);
        } else if (problem?.existing) {
          this.existing.set(problem.existing);
        } else if (problem?.reasons) {
          this.reasons.set(problem.reasons);
        } else if (problem?.detail) {
          this.error.set(problem.detail);
        }
      },
    });
  }

  protected cancel(): void {
    this.dialog.close();
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  private problem(error: unknown): Problem | null {
    return error instanceof HttpErrorResponse && (error.status === 409 || error.status === 422 || error.status === 502) ? ((error.error as Problem | null) ?? null) : null;
  }
}

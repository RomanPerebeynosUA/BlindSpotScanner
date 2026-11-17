import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { RemediationApi } from '../../../core/api/remediation-api';
import { JiraDraft, RemediationAction } from '../../../core/api/api-types';

export interface JiraDialogData {
  runId: string;
  findingId: string;
}

const PRIORITIES = ['Highest', 'High', 'Medium', 'Low'] as const;

/**
 * Doc 04 Jira dialog: the server's draft, editable, created only on the user's click (principle 8).
 * A finding that already has a ticket shows it, and "Create anyway" sits behind that warning.
 * Closes with the created ticket, or with nothing.
 */
@Component({
  selector: 'app-jira-dialog',
  templateUrl: './jira-dialog.html',
  styleUrl: './jira-dialog.scss',
})
export class JiraDialog {
  private readonly api = inject(RemediationApi);
  private readonly dialog = inject<MatDialogRef<JiraDialog, RemediationAction>>(MatDialogRef);
  private readonly data = inject<JiraDialogData>(MAT_DIALOG_DATA);

  protected readonly priorities = PRIORITIES;
  protected readonly draft = signal<JiraDraft | null>(null);
  protected readonly existing = signal<RemediationAction | null>(null);
  protected readonly summary = signal('');
  protected readonly description = signal('');
  protected readonly priority = signal('');
  protected readonly labels = signal('');
  protected readonly busy = signal(false);

  constructor() {
    this.api.jiraPreview(this.data.runId, this.data.findingId).subscribe({
      next: (draft) => {
        this.draft.set(draft);
        this.existing.set(draft.existing);
        this.summary.set(draft.summary);
        this.description.set(draft.description);
        this.priority.set(draft.priority);
        this.labels.set(draft.labels.join(', '));
      },
      // The interceptor shows why (for example "Jira not configured"); there is nothing to edit.
      error: () => this.dialog.close(),
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const draft = this.draft();
    if (!draft || !this.summary().trim() || this.busy()) {
      return;
    }

    this.busy.set(true);
    const body = {
      summary: this.summary().trim(),
      description: this.description(),
      projectKey: draft.projectKey,
      issueType: draft.issueType,
      priority: this.priority(),
      labels: this.labels().split(',').map((l) => l.trim()).filter((l) => l.length > 0),
      force: this.existing() !== null,
    };
    this.api.createJira(this.data.runId, this.data.findingId, body).subscribe({
      next: (action) => this.dialog.close(action),
      error: (error: unknown) => {
        this.busy.set(false);
        const existing = error instanceof HttpErrorResponse && error.status === 409 ? (error.error as { existing?: RemediationAction } | null)?.existing : undefined;
        if (existing) {
          this.existing.set(existing);
        }
      },
    });
  }

  protected cancel(): void {
    this.dialog.close();
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }
}

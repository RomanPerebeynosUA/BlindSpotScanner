import { Component, computed, inject, input } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Finding } from '../../../core/api/report';
import { RemediationAction } from '../../../core/api/api-types';
import { ReportStore } from '../report-store';
import { JiraDialog, JiraDialogData } from '../jira-dialog/jira-dialog';

/** A finding's Jira control: its ticket as "KEY · status", or a "Create Jira" button that opens the dialog (doc 04). */
@Component({
  selector: 'app-jira-action',
  template: `@if (ticket(); as t) {
      <a class="chip ticket" [href]="t.url" target="_blank" rel="noopener">{{ t.externalKey }} · {{ t.status }}</a>
    } @else {
      <button type="button" class="btn sm jira" [attr.aria-label]="'Create Jira ticket: ' + finding().headline" (click)="create()">Create Jira</button>
    }`,
})
export class JiraAction {
  private readonly store = inject(ReportStore);
  private readonly dialog = inject(MatDialog);

  readonly finding = input.required<Finding>();

  protected readonly ticket = computed(() => this.store.ticketFor(this.finding().stableKey));

  protected create(): void {
    const runId = this.store.report()?.meta.runId;
    if (!runId) {
      return;
    }

    const data: JiraDialogData = { runId, findingId: this.finding().id };
    this.dialog
      .open<JiraDialog, JiraDialogData, RemediationAction>(JiraDialog, { data, maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'jira-title' })
      .afterClosed()
      .subscribe((created) => {
        if (created) {
          this.store.actions.update((actions) => [created, ...actions]);
        }
      });
  }
}

import { Component, computed, inject, input } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Finding, FindingKind } from '../../../core/api/report';
import { RemediationAction } from '../../../core/api/api-types';
import { ReportStore } from '../report-store';
import { FixDialog, FixDialogData } from '../fix-dialog/fix-dialog';

/** The kinds a single test can fix (D25); seams and risks stay Jira-only. */
const FIXABLE: readonly FindingKind[] = ['UncoveredEndpoint', 'WeakTest', 'FalsePositive', 'SkippedTest'];

/** A finding's fix-PR control: its PR as "#7 · status", or "Fix with PR" (hidden in read-only mode, D25). */
@Component({
  selector: 'app-fix-action',
  template: `@if (pullRequest(); as pr) {
      <a class="chip pr" [href]="pr.url" target="_blank" rel="noopener">{{ pr.externalKey }} · {{ pr.status }}</a>
    } @else if (offered()) {
      <button type="button" class="btn sm fix" [attr.aria-label]="'Fix with PR: ' + finding().headline" (click)="open()">Fix with PR</button>
    }`,
})
export class FixAction {
  private readonly store = inject(ReportStore);
  private readonly dialog = inject(MatDialog);

  readonly finding = input.required<Finding>();

  protected readonly pullRequest = computed(() => this.store.pullRequestFor(this.finding().stableKey));
  protected readonly offered = computed(() => this.store.features().pullRequests && FIXABLE.includes(this.finding().kind));

  protected open(): void {
    const runId = this.store.report()?.meta.runId;
    if (!runId) {
      return;
    }

    const data: FixDialogData = { runId, findingId: this.finding().id };
    this.dialog
      .open<FixDialog, FixDialogData, RemediationAction>(FixDialog, { data, maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'fix-title' })
      .afterClosed()
      .subscribe((created) => {
        if (created) {
          this.store.actions.update((actions) => [created, ...actions]);
        }
      });
  }
}

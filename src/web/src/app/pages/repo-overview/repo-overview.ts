import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { filter, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth';
import { LlmSharingDialog } from './llm-sharing-dialog/llm-sharing-dialog';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { Repository, RunHistoryItem } from '../../core/api/api-types';
import { StatusChip } from '../../shared/status-chip/status-chip';
import { Sparkline } from '../../shared/sparkline/sparkline';

const TREND_LENGTH = 12;

/** `/repos/:id`: run history and "Scan now" (doc 04). */
@Component({
  selector: 'app-repo-overview',
  imports: [RouterLink, DatePipe, DecimalPipe, StatusChip, Sparkline],
  templateUrl: './repo-overview.html',
  styleUrl: './repo-overview.scss',
})
export class RepoOverview implements OnInit {
  private readonly api = inject(RepositoriesApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  protected readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly repository = signal<Repository | null>(null);
  protected readonly runs = signal<RunHistoryItem[] | null>(null);
  protected readonly starting = signal(false);
  /** The consent dialog or its acknowledgement is in progress, so Scan now waits. */
  protected readonly consenting = signal(false);

  /** The last 12 succeeded runs, oldest first (history arrives newest first), matching the repositories list's trend (D23). */
  private readonly succeeded = computed(() =>
    (this.runs() ?? []).filter((r) => r.status === 'Succeeded').slice(0, TREND_LENGTH).reverse(),
  );
  protected readonly scoreTrend = computed(() => this.succeeded().map((r) => r.score));
  protected readonly coveredTrend = computed(() =>
    this.succeeded().map((r) => (r.endpointsTotal ? (r.endpointsCovered ?? 0) / r.endpointsTotal : null)),
  );

  ngOnInit(): void {
    this.api.get(this.id()).subscribe((repository) => this.repository.set(repository));
    this.api.runs(this.id()).subscribe((runs) => this.runs.set(runs));
  }

  /** The first scan of a repository asks for LLM data-sharing consent (doc 10) and records it. */
  protected scanNow(): void {
    if (this.starting() || this.consenting() || !this.repository()) {
      return;
    }

    if (this.repository()?.llmSharingAcknowledged === false) {
      this.consenting.set(true);
      this.dialog
        .open<LlmSharingDialog, unknown, boolean>(LlmSharingDialog, { maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'llm-title' })
        .afterClosed()
        .pipe(
          filter((agreed) => agreed === true),
          switchMap(() => this.api.acknowledgeLlmSharing(this.id())),
        )
        .subscribe({
          next: () => {
            this.consenting.set(false);
            this.repository.update((r) => (r ? { ...r, llmSharingAcknowledged: true } : r));
            this.start();
          },
          // Declined (the filter completes without a value) or the acknowledgement failed (the interceptor says why).
          complete: () => this.consenting.set(false),
          error: () => this.consenting.set(false),
        });
      return;
    }

    this.start();
  }

  private start(): void {
    this.starting.set(true);
    this.api.startScan(this.id()).subscribe({
      next: ({ runId }) => void this.router.navigate(['/runs', runId, 'live']),
      error: () => this.starting.set(false),
    });
  }

  protected link(run: RunHistoryItem): string[] {
    return run.status === 'Queued' || run.status === 'Running' ? ['/runs', run.runId, 'live'] : ['/runs', run.runId];
  }
}

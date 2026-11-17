import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { Repository, RunHistoryItem } from '../../core/api/api-types';
import { StatusChip } from '../../shared/status-chip/status-chip';
import { Sparkline } from '../../shared/sparkline/sparkline';

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

  readonly id = input.required<string>();

  protected readonly repository = signal<Repository | null>(null);
  protected readonly runs = signal<RunHistoryItem[] | null>(null);
  protected readonly starting = signal(false);

  /** Succeeded runs, oldest first (history arrives newest first). */
  private readonly succeeded = computed(() => (this.runs() ?? []).filter((r) => r.status === 'Succeeded').reverse());
  protected readonly scoreTrend = computed(() => this.succeeded().map((r) => r.score));
  protected readonly coveredTrend = computed(() =>
    this.succeeded().map((r) => (r.endpointsTotal ? (r.endpointsCovered ?? 0) / r.endpointsTotal : null)),
  );

  ngOnInit(): void {
    this.api.get(this.id()).subscribe((repository) => this.repository.set(repository));
    this.api.runs(this.id()).subscribe((runs) => this.runs.set(runs));
  }

  protected scanNow(): void {
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

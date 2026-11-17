import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { RunsApi } from '../../core/api/runs-api';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { isReport, RunHistoryItem, RunStatusBody } from '../../core/api/api-types';
import { DeltaChanges } from '../../shared/delta-changes/delta-changes';
import { commitUrl, ReportStore } from './report-store';
import { SummaryCard } from './summary-card/summary-card';
import { TopRisks } from './top-risks/top-risks';
import { EndpointCoverage } from './endpoint-coverage/endpoint-coverage';
import { Scorecard } from './scorecard/scorecard';
import { Findings } from './findings/findings';

/**
 * `/runs/:runId`: the report (doc 04 sections 1–6) with the delta against the previous scan, the
 * comparability banner and a run picker (doc 08, M8). Jira/PR actions and the actions log are M9/M10.
 */
@Component({
  selector: 'app-report',
  imports: [DatePipe, RouterLink, SummaryCard, TopRisks, EndpointCoverage, Scorecard, Findings, DeltaChanges],
  providers: [ReportStore],
  templateUrl: './report.html',
  styleUrl: './report.scss',
})
export class Report {
  private readonly runs = inject(RunsApi);
  private readonly repositories = inject(RepositoriesApi);
  private readonly router = inject(Router);
  protected readonly store = inject(ReportStore);

  readonly runId = input.required<string>();

  protected readonly outcome = signal<RunStatusBody | null>(null);
  protected readonly notFound = signal(false);
  protected readonly commitUrl = commitUrl;

  /** The repository's succeeded runs, newest first (for the run picker). */
  protected readonly siblings = signal<RunHistoryItem[]>([]);
  private readonly position = computed(() => this.siblings().findIndex((r) => r.runId === this.runId()));
  protected readonly newer = computed(() => (this.position() > 0 ? this.siblings()[this.position() - 1] : null));
  protected readonly older = computed(() => {
    const i = this.position();
    return i >= 0 && i < this.siblings().length - 1 ? this.siblings()[i + 1] : null;
  });

  /** The baseline for "Compare with…": the stored delta's base, else the next older run. */
  protected readonly compareBase = computed(() => this.store.report()?.delta?.baseRunId ?? this.older()?.runId ?? null);

  constructor() {
    // The run picker navigates between runs on this same route, so react to every runId.
    effect(() => {
      const runId = this.runId();
      untracked(() => this.load(runId));
    });
  }

  protected go(runId: string): void {
    void this.router.navigate(['/runs', runId]);
  }

  protected pick(event: Event): void {
    this.go((event.target as HTMLSelectElement).value);
  }

  private load(runId: string): void {
    this.store.report.set(null);
    this.outcome.set(null);
    this.notFound.set(false);
    this.runs.get(runId).subscribe({
      next: (body) => {
        if (isReport(body)) {
          this.store.report.set(body);
        } else if (body.status === 'Queued' || body.status === 'Running') {
          void this.router.navigate(['/runs', runId, 'live'], { replaceUrl: true });
        } else {
          this.outcome.set(body);
        }
      },
      error: (error: unknown) => this.notFound.set(error instanceof HttpErrorResponse && error.status === 404),
    });
    this.runs.repository(runId).subscribe({
      next: (repository) =>
        this.repositories.runs(repository.id).subscribe((history) => this.siblings.set(history.filter((r) => r.status === 'Succeeded'))),
      error: () => this.siblings.set([]),
    });
  }
}

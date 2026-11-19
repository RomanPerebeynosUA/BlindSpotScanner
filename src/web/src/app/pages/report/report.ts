import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, forkJoin, map, Observable, of, switchMap, tap } from 'rxjs';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { RunsApi } from '../../core/api/runs-api';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { RemediationApi } from '../../core/api/remediation-api';
import { isReport, RemediationAction, RunBody, RunHistoryItem, RunStatusBody } from '../../core/api/api-types';
import { DeltaChanges } from '../../shared/delta-changes/delta-changes';
import { commitUrl, ReportStore } from './report-store';
import { SummaryCard } from './summary-card/summary-card';
import { TopRisks } from './top-risks/top-risks';
import { EndpointCoverage } from './endpoint-coverage/endpoint-coverage';
import { Scorecard } from './scorecard/scorecard';
import { Findings } from './findings/findings';
import { ActionsLog } from './actions-log/actions-log';

/**
 * `/runs/:runId`: the report (doc 04 sections 1–6) with the delta against the previous scan, the
 * comparability banner and a run picker (doc 08, M8). Jira/PR actions and the actions log are M9/M10.
 */
@Component({
  selector: 'app-report',
  imports: [DatePipe, RouterLink, SummaryCard, TopRisks, EndpointCoverage, Scorecard, Findings, DeltaChanges, ActionsLog],
  providers: [ReportStore],
  templateUrl: './report.html',
  styleUrl: './report.scss',
})
export class Report {
  private readonly runs = inject(RunsApi);
  private readonly repositories = inject(RepositoriesApi);
  private readonly remediation = inject(RemediationApi);
  private readonly router = inject(Router);
  protected readonly store = inject(ReportStore);

  readonly runId = input.required<string>();

  protected readonly outcome = signal<RunStatusBody | null>(null);
  protected readonly notFound = signal(false);
  /** The run's repository, for the link back to its scans. */
  protected readonly repositoryId = signal<string | null>(null);
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
    // Read once: whether fix PRs may be offered (read-only mode hides them, D25).
    this.remediation
      .features()
      .pipe(catchError(() => of({ pullRequests: false, jira: false })), takeUntilDestroyed())
      .subscribe((features) => this.store.features.set(features));
    // The run picker navigates between runs on this same route. switchMap drops the answer for a run
    // the user already left, so a late reply can't show the wrong report or redirect to its live page.
    const runId$ = toObservable(this.runId);
    runId$
      .pipe(
        tap(() => this.reset()),
        switchMap((runId) => this.read(runId)),
        takeUntilDestroyed(),
      )
      .subscribe(({ runId, body, error }) => this.show(runId, body, error));
    runId$
      .pipe(
        switchMap((runId) =>
          this.runs.repository(runId).pipe(
            // The run picker's history and the repository's tickets (D24); a failed list just stays empty.
            tap((repository) => this.repositoryId.set(repository.id)),
            switchMap((repository) =>
              forkJoin({
                history: this.repositories.runs(repository.id).pipe(catchError(() => of<RunHistoryItem[]>([]))),
                actions: this.remediation.actions(repository.id).pipe(catchError(() => of<RemediationAction[]>([]))),
              }),
            ),
            catchError(() => of({ history: [] as RunHistoryItem[], actions: [] as RemediationAction[] })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ history, actions }) => {
        this.siblings.set(history.filter((r) => r.status === 'Succeeded'));
        // Merged, not replaced: a ticket created while this (status-refreshing) load was slow stays.
        this.store.actions.update((local) => [...local.filter((a) => !actions.some((l) => l.id === a.id)), ...actions]);
      });
  }

  protected go(runId: string): void {
    void this.router.navigate(['/runs', runId]);
  }

  protected pick(event: Event): void {
    this.go((event.target as HTMLSelectElement).value);
  }

  private reset(): void {
    this.store.report.set(null);
    this.store.statusFilter.set('all');
    this.store.search.set('');
    this.store.actions.set([]);
    this.outcome.set(null);
    this.notFound.set(false);
  }

  private read(runId: string): Observable<{ runId: string; body?: RunBody; error?: unknown }> {
    return this.runs.get(runId).pipe(
      map((body) => ({ runId, body })),
      catchError((error: unknown) => of({ runId, error })),
    );
  }

  private show(runId: string, body: RunBody | undefined, error: unknown): void {
    if (body === undefined) {
      this.notFound.set(error instanceof HttpErrorResponse && error.status === 404);
    } else if (isReport(body)) {
      this.store.report.set(body);
    } else if (body.status === 'Queued' || body.status === 'Running') {
      void this.router.navigate(['/runs', runId, 'live'], { replaceUrl: true });
    } else {
      this.outcome.set(body);
    }
  }
}

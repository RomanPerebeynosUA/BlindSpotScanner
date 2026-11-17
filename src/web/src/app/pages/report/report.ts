import { Component, inject, input, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { RunsApi } from '../../core/api/runs-api';
import { isReport, RunStatusBody } from '../../core/api/api-types';
import { commitUrl, ReportStore } from './report-store';
import { SummaryCard } from './summary-card/summary-card';
import { TopRisks } from './top-risks/top-risks';
import { EndpointCoverage } from './endpoint-coverage/endpoint-coverage';
import { Scorecard } from './scorecard/scorecard';
import { Findings } from './findings/findings';

/**
 * `/runs/:runId`: the report (doc 04 sections 1–6). The run picker, compare and export are M8/M11;
 * Jira/PR actions and the actions log are M9/M10.
 */
@Component({
  selector: 'app-report',
  imports: [DatePipe, SummaryCard, TopRisks, EndpointCoverage, Scorecard, Findings],
  providers: [ReportStore],
  templateUrl: './report.html',
  styleUrl: './report.scss',
})
export class Report implements OnInit {
  private readonly runs = inject(RunsApi);
  private readonly router = inject(Router);
  protected readonly store = inject(ReportStore);

  readonly runId = input.required<string>();

  protected readonly outcome = signal<RunStatusBody | null>(null);
  protected readonly notFound = signal(false);
  protected readonly commitUrl = commitUrl;

  ngOnInit(): void {
    this.runs.get(this.runId()).subscribe({
      next: (body) => {
        if (isReport(body)) {
          this.store.report.set(body);
        } else if (body.status === 'Queued' || body.status === 'Running') {
          void this.router.navigate(['/runs', this.runId(), 'live'], { replaceUrl: true });
        } else {
          this.outcome.set(body);
        }
      },
      error: (error: unknown) => this.notFound.set(error instanceof HttpErrorResponse && error.status === 404),
    });
  }
}

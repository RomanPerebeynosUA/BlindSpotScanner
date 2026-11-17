import { Component, inject, input, signal } from '@angular/core';
import { COVERAGE_STATUSES, EndpointCoverage as EndpointResult, Finding, ReportMeta } from '../../../core/api/report';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { EvidenceLink } from '../../../shared/evidence-link/evidence-link';
import { ReportStore, StatusFilter } from '../report-store';
import { JiraAction } from '../jira-action/jira-action';
import { FixAction } from '../fix-action/fix-action';

const FILTER_LABELS: Record<StatusFilter, string> = {
  all: 'All',
  covered: '✅ Covered',
  weak: '⚠️ Weak',
  skipped: '⏭️ Skipped',
  none: '❌ None',
  unverified: '❔ Unverified',
};

/** Endpoint coverage grouped by controller, with status filters, search and row drill-down (doc 04). */
@Component({
  selector: 'app-endpoint-coverage',
  imports: [StatusChip, EvidenceLink, JiraAction, FixAction],
  templateUrl: './endpoint-coverage.html',
  styleUrl: './endpoint-coverage.scss',
})
export class EndpointCoverage {
  protected readonly store = inject(ReportStore);

  readonly meta = input.required<ReportMeta>();

  protected readonly filters: readonly StatusFilter[] = ['all', ...COVERAGE_STATUSES];
  protected readonly labels = FILTER_LABELS;
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  /** An untested endpoint's uncovered-endpoint finding (its first detail is `METHOD route`), for the Jira control (D24). */
  protected findingFor(e: EndpointResult): Finding | undefined {
    return e.status === 'none'
      ? this.store.report()?.findings.find((f) => f.kind === 'UncoveredEndpoint' && f.detail?.[0] === `${e.method} ${e.route}`)
      : undefined;
  }

  /** Unique per row: the same route can be declared twice (D14), so the position is part of the key. */
  protected key(controller: string, sourcePath: string, index: number, method: string, route: string): string {
    return `${controller}|${sourcePath}|${index}|${method} ${route}`;
  }

  protected toggle(key: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (!next.delete(key)) {
        next.add(key);
      }

      return next;
    });
  }

  protected setSearch(event: Event): void {
    this.store.search.set((event.target as HTMLInputElement).value);
  }
}

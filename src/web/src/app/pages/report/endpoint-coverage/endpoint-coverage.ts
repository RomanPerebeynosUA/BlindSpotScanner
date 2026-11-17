import { Component, inject, input, signal } from '@angular/core';
import { COVERAGE_STATUSES, ReportMeta } from '../../../core/api/report';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { EvidenceLink } from '../../../shared/evidence-link/evidence-link';
import { ReportStore, StatusFilter } from '../report-store';

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
  imports: [StatusChip, EvidenceLink],
  templateUrl: './endpoint-coverage.html',
  styleUrl: './endpoint-coverage.scss',
})
export class EndpointCoverage {
  protected readonly store = inject(ReportStore);

  readonly meta = input.required<ReportMeta>();

  protected readonly filters: readonly StatusFilter[] = ['all', ...COVERAGE_STATUSES];
  protected readonly labels = FILTER_LABELS;
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected key(controller: string, method: string, route: string): string {
    return `${controller}|${method} ${route}`;
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

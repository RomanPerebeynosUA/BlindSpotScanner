import { computed, Injectable, signal } from '@angular/core';
import { ControllerCoverage, COVERAGE_STATUSES, CoverageStatus, IntegrationReport, ReportMeta } from '../../core/api/report';
import { Features, RemediationAction } from '../../core/api/api-types';

export type StatusFilter = CoverageStatus | 'all';

/** Per-route state of the report page: the report plus the coverage table's filter and search (doc 04). */
@Injectable()
export class ReportStore {
  readonly report = signal<IntegrationReport | null>(null);
  readonly statusFilter = signal<StatusFilter>('all');
  readonly search = signal('');

  /** The repository's tickets, newest first (D24); they outlive runs, so they match findings by stable key. */
  readonly actions = signal<RemediationAction[]>([]);

  /** What the UI may offer (D25); fix PRs stay hidden until the API says otherwise. */
  readonly features = signal<Features>({ pullRequests: false, jira: false });

  /** The newest fix PR for a stable key, if any. */
  pullRequestFor(stableKey: string): RemediationAction | undefined {
    return this.actions().find((a) => a.type === 'PullRequest' && a.stableKey === stableKey);
  }

  /** The newest Jira ticket for a stable key, if any. */
  ticketFor(stableKey: string): RemediationAction | undefined {
    return this.actions().find((a) => a.type === 'JiraTicket' && a.stableKey === stableKey);
  }

  /** Endpoint counts per status, for the filter buttons. */
  readonly statusCounts = computed(() => {
    const endpoints = (this.report()?.endpointCoverage ?? []).flatMap((c) => c.endpoints);
    const counts = { all: endpoints.length } as Record<StatusFilter, number>;
    for (const status of COVERAGE_STATUSES) {
      counts[status] = endpoints.filter((e) => e.status === status).length;
    }

    return counts;
  });

  /** Controllers with at least one endpoint matching the filter and search, holding only those endpoints. */
  readonly visibleControllers = computed<ControllerCoverage[]>(() => {
    const filter = this.statusFilter();
    const term = this.search().trim().toLowerCase();
    return (this.report()?.endpointCoverage ?? [])
      .map((c) => ({
        ...c,
        endpoints: c.endpoints.filter(
          (e) =>
            (filter === 'all' || e.status === filter) &&
            (term === '' ||
              `${e.method} ${e.route}`.toLowerCase().includes(term) ||
              c.controller.toLowerCase().includes(term) ||
              e.evidence.some((citation) => citation.toLowerCase().includes(term))),
        ),
      }))
      .filter((c) => c.endpoints.length > 0);
  });
}

/** The cited file on GitHub at the scan's pinned commit (`path::Name` → blob URL of `path`). */
export function evidenceUrl(meta: ReportMeta, citation: string): string {
  const path = citation.split('::')[0];
  const encoded = path.split('/').map(encodeURIComponent).join('/');
  return `${meta.repo.url}/blob/${meta.commitSha}/${encoded}`;
}

export function commitUrl(meta: ReportMeta): string {
  return `${meta.repo.url}/commit/${meta.commitSha}`;
}

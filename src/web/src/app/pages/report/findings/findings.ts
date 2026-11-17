import { Component, computed, input } from '@angular/core';
import { Finding, FindingKind, ReportMeta } from '../../../core/api/report';
import { EvidenceLink } from '../../../shared/evidence-link/evidence-link';
import { JiraAction } from '../jira-action/jira-action';
import { FixAction } from '../fix-action/fix-action';

const GROUPS: readonly { kind: FindingKind; title: string }[] = [
  { kind: 'FalsePositive', title: 'False positives' },
  { kind: 'SkippedTest', title: 'Skipped tests' },
  { kind: 'UncoveredSeam', title: 'Uncovered seams' },
];

/** False positives, skipped tests and uncovered seams (doc 04 section 6); uncovered endpoints are in the coverage table. */
@Component({
  selector: 'app-findings',
  imports: [EvidenceLink, JiraAction, FixAction],
  templateUrl: './findings.html',
})
export class Findings {
  readonly findings = input.required<Finding[]>();
  readonly meta = input.required<ReportMeta>();

  protected readonly groups = computed(() => GROUPS.map((g) => ({ ...g, items: this.findings().filter((f) => f.kind === g.kind) })));
}

import { Component, computed, input } from '@angular/core';
import { RunStatus } from '../../core/api/api-types';
import { CoverageStatus } from '../../core/api/report';

const COVERAGE_LABELS: Record<CoverageStatus, string> = {
  covered: '✅ Covered',
  weak: '⚠️ Weak',
  skipped: '⏭️ Skipped',
  none: '❌ None',
  unverified: '❔ Unverified',
};

/** A status as icon plus text, never colour alone (doc 04). */
@Component({
  selector: 'app-status-chip',
  templateUrl: './status-chip.html',
  styleUrl: './status-chip.scss',
})
export class StatusChip {
  readonly status = input.required<CoverageStatus | RunStatus>();

  protected readonly label = computed(() => COVERAGE_LABELS[this.status() as CoverageStatus] ?? this.status());
  protected readonly cssClass = computed(() => `chip s-${this.status().toLowerCase()}`);
}

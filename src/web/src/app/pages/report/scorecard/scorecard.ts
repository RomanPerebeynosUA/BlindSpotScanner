import { Component, computed, input, signal } from '@angular/core';
import { PercentPipe } from '@angular/common';
import { ReportMeta, ScorecardRow } from '../../../core/api/report';
import { EvidenceLink } from '../../../shared/evidence-link/evidence-link';

/** The 7 rubric dimensions; an unscorable one is N/A, never 0 (principle 5). */
@Component({
  selector: 'app-scorecard',
  imports: [PercentPipe, EvidenceLink],
  templateUrl: './scorecard.html',
})
export class Scorecard {
  readonly rows = input.required<ScorecardRow[]>();
  readonly meta = input.required<ReportMeta>();
  readonly scoreLabel = input.required<string>();

  protected readonly sorted = computed(() => [...this.rows()].sort((a, b) => a.id - b.id));
  protected readonly expanded = signal<number | null>(null);

  protected toggle(id: number): void {
    this.expanded.update((current) => (current === id ? null : id));
  }
}

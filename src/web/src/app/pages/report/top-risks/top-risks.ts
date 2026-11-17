import { Component, computed, input } from '@angular/core';
import { ReportMeta, TopRisk } from '../../../core/api/report';
import { EvidenceLink } from '../../../shared/evidence-link/evidence-link';

/** Ranked risks: headline and why-it-matters visible, detail and evidence on expand (doc 04). */
@Component({
  selector: 'app-top-risks',
  imports: [EvidenceLink],
  templateUrl: './top-risks.html',
  styleUrl: './top-risks.scss',
})
export class TopRisks {
  readonly risks = input.required<TopRisk[]>();
  readonly meta = input.required<ReportMeta>();

  protected readonly ranked = computed(() => [...this.risks()].sort((a, b) => a.rank - b.rank));
}

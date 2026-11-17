import { Component, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { DeltaMetric, IntegrationReport } from '../../../core/api/report';
import { DeltaChip } from '../../../shared/delta-chip/delta-chip';

/** Verdict, the computed bullets and the KPI tiles; every value is the backend's summary (principle 2). */
@Component({
  selector: 'app-summary-card',
  imports: [DecimalPipe, DeltaChip],
  templateUrl: './summary-card.html',
})
export class SummaryCard {
  readonly report = input.required<IntegrationReport>();

  /** A metric of the stored delta against the previous scan (none on a first scan). */
  protected metric(name: string): DeltaMetric | undefined {
    return this.report().delta?.metrics.find((m) => m.name === name);
  }
}

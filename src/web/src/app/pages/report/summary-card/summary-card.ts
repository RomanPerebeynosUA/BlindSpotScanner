import { Component, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { IntegrationReport } from '../../../core/api/report';

/** Verdict, the computed bullets and the KPI tiles; every value is the backend's summary (principle 2). */
@Component({
  selector: 'app-summary-card',
  imports: [DecimalPipe],
  templateUrl: './summary-card.html',
})
export class SummaryCard {
  readonly report = input.required<IntegrationReport>();
}

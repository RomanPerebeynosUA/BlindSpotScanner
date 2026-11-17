import { Component, computed, input } from '@angular/core';
import { DeltaMetric } from '../../core/api/report';

/**
 * A KPI change chip (prototype `.delta`): ▲ for a positive change, ▼ for a negative one; coloured by
 * the backend's direction, and neutral when the runs aren't comparable (principle 6).
 */
@Component({
  selector: 'app-delta-chip',
  // The direction also goes in hidden text, because screen readers often skip a title.
  template: `@if (text(); as t) {<span [class]="cssClass()" [attr.title]="title()">{{ t }}</span><span class="sr-only">{{ title() }}</span>}`,
})
export class DeltaChip {
  readonly metric = input<DeltaMetric | undefined>();
  readonly comparable = input(true);
  readonly decimals = input(0);

  protected readonly text = computed(() => {
    const change = this.metric()?.change;
    if (change === null || change === undefined || change === 0) {
      return null;
    }

    return `${change > 0 ? '▲' : '▼'} ${Math.abs(change).toFixed(this.decimals())}`;
  });

  protected readonly cssClass = computed(() => {
    const direction = this.metric()?.direction;
    const tone = !this.comparable() || direction === 'unchanged' ? 'flat' : direction === 'improved' ? 'up' : 'down';
    return `delta ${tone}`;
  });

  protected readonly title = computed(() =>
    this.comparable() ? `${this.metric()?.direction} since the previous scan` : 'Not comparable with the previous scan',
  );
}

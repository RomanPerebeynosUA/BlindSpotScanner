import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RemediationAction } from '../../../core/api/api-types';

/** Doc 04 section 7: the tickets (and, from M10, PRs) created from this repository, with their live status. */
@Component({
  selector: 'app-actions-log',
  imports: [DatePipe],
  templateUrl: './actions-log.html',
})
export class ActionsLog {
  readonly actions = input.required<RemediationAction[]>();
}

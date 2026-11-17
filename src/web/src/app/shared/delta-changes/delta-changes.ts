import { Component, input } from '@angular/core';
import { RunDelta } from '../../core/api/report';

/** What changed between two runs (doc 08 "Changes since"), straight from the backend's delta. */
@Component({
  selector: 'app-delta-changes',
  templateUrl: './delta-changes.html',
})
export class DeltaChanges {
  readonly delta = input.required<RunDelta>();
}

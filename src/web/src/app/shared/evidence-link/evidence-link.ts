import { Component, computed, input } from '@angular/core';
import { ReportMeta } from '../../core/api/report';
import { evidenceUrl } from '../../pages/report/report-store';

/** A `path::Name` citation linking the cited file at the scan's pinned commit. */
@Component({
  selector: 'app-evidence-link',
  template: `<a class="ev" [href]="url()" target="_blank" rel="noopener">{{ citation() }}</a>`,
})
export class EvidenceLink {
  readonly meta = input.required<ReportMeta>();
  readonly citation = input.required<string>();

  protected readonly url = computed(() => evidenceUrl(this.meta(), this.citation()));
}

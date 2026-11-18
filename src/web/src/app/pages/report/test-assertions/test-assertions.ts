import { Component, computed, input } from '@angular/core';
import { AssertionLevels, ReportMeta, TestAssertion } from '../../../core/api/report';
import { EvidenceLink } from '../../../shared/evidence-link/evidence-link';

const LABELS = ['no assertion', 'status only', 'shape only', 'response values', 'saved state', 'side effects'];

/** Assertion depth per integration test (D27), weakest first; every value comes from the backend. */
@Component({
  selector: 'app-test-assertions',
  imports: [EvidenceLink],
  templateUrl: './test-assertions.html',
})
export class TestAssertions {
  readonly tests = input.required<TestAssertion[]>();
  readonly levels = input.required<AssertionLevels>();
  readonly meta = input.required<ReportMeta>();

  /** Level ascending with unknown last, then by citation. */
  protected readonly sorted = computed(() =>
    [...this.tests()].sort((a, b) => (a.level ?? 6) - (b.level ?? 6) || a.test.localeCompare(b.test)),
  );

  protected level(test: TestAssertion): string {
    return test.level === null ? 'unknown' : `L${test.level} · ${LABELS[test.level]}`;
  }

  protected unchecked(test: TestAssertion): string {
    if (!test.fieldsResolved) {
      return 'unresolved';
    }

    return test.uncheckedFields.length > 0 ? test.uncheckedFields.join(', ') : '—';
  }
}

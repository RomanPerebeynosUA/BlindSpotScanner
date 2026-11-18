import { TestBed } from '@angular/core/testing';
import { AssertionLevels, TestAssertion } from '../../../core/api/report';
import { sampleReport } from '../../../../testing/sample-report';
import { TestAssertions } from './test-assertions';

const row = (test: string, level: number | null, change: Partial<TestAssertion> = {}): TestAssertion => ({
  test, level, levelSource: level === null ? 'unknown' : 'code', errorPath: false,
  sentFields: [], checkedFields: [], uncheckedFields: [], fieldsResolved: true, ...change,
});

const levels: AssertionLevels = { l0: 1, l1: 1, l2: 0, l3: 1, l4: 0, l5: 0, unknown: 1 };

function render(tests: TestAssertion[]): HTMLElement {
  TestBed.configureTestingModule({ imports: [TestAssertions] });
  const fixture = TestBed.createComponent(TestAssertions);
  fixture.componentRef.setInput('tests', tests);
  fixture.componentRef.setInput('levels', levels);
  fixture.componentRef.setInput('meta', sampleReport().meta);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const cells = (el: HTMLElement, column: number) =>
  [...el.querySelectorAll('tbody tr')].map((tr) => tr.querySelectorAll('td')[column]?.textContent?.trim());

describe('TestAssertions', () => {
  it('assertion table sorts weakest first, unknown last', () => {
    // Act
    const el = render([row('t/A.cs::A.c', 3), row('t/A.cs::A.u', null), row('t/A.cs::A.b', 1), row('t/A.cs::A.a', 0)]);

    // Assert
    expect(cells(el, 0)).toEqual(['t/A.cs::A.a', 't/A.cs::A.b', 't/A.cs::A.c', 't/A.cs::A.u']);
  });

  it('assertion table labels each level', () => {
    // Act
    const el = render([row('t/A.cs::A.a', 4, { readBack: 'api' }), row('t/A.cs::A.u', null)]);

    // Assert
    expect([cells(el, 1), cells(el, 2)]).toEqual([['L4 · saved state', 'unknown'], ['api', '—']]);
  });

  it('assertion table shows unchecked fields and unresolved', () => {
    // Act
    const el = render([
      row('t/A.cs::A.a', 3, { uncheckedFields: ['lines[].bookId', 'lines[].quantity'] }),
      row('t/A.cs::A.b', 3, { fieldsResolved: false }),
      row('t/A.cs::A.c', 3),
    ]);

    // Assert
    expect(cells(el, 3)).toEqual(['lines[].bookId, lines[].quantity', 'unresolved', '—']);
  });

  it('assertion counts line shows the summary levels', () => {
    // Act
    const el = render([row('t/A.cs::A.a', 3)]);

    // Assert
    expect(el.querySelector('.levels')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('L0 1 · L1 1 · L2 0 · L3 1 · L4 0 · L5 0 · unknown 1');
  });
});

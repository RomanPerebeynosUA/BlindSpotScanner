import { TestBed } from '@angular/core/testing';
import { Sparkline } from './sparkline';

function render(values: (number | null)[], max = 10, label = 'Score trend'): HTMLElement {
  const fixture = TestBed.createComponent(Sparkline);
  fixture.componentRef.setInput('values', values);
  fixture.componentRef.setInput('max', max);
  fixture.componentRef.setInput('label', label);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Sparkline', () => {
  it('sparkline_twoPoints_drawsLineAndLastDot', () => {
    // Act
    const el = render([4.7, 5.1]);

    // Assert
    expect(el.querySelectorAll('path.line').length).toBe(1);
    const dot = el.querySelector('circle') as SVGCircleElement;
    expect([dot.getAttribute('cx'), dot.getAttribute('cy')]).toEqual(['114', '16.7']);
  });

  it('sparkline_onePoint_drawsDotOnly', () => {
    // Act
    const el = render([5.1]);

    // Assert
    expect([el.querySelectorAll('path.line').length, el.querySelectorAll('circle').length]).toEqual([0, 1]);
  });

  it('sparkline_isolatedPointBetweenGaps_drawsItsDot', () => {
    // Act (your TodoApp trend: N/A, N/A, 5.6, N/A, 5.7, 5.9)
    const el = render([null, null, 5.6, null, 5.7, 5.9]);

    // Assert (the lone 5.6 and the last value each get a dot)
    expect([...el.querySelectorAll('circle')].map((c) => c.getAttribute('cx'))).toEqual(['49.2', '114']);
  });

  it('sparkline_noValues_rendersNothing', () => {
    // Act
    const el = render([null, null]);

    // Assert
    expect(el.querySelector('svg')).toBeNull();
  });

  it('sparkline_nullPoint_isAGap', () => {
    // Act
    const el = render([4, 5, null, 6, 7]);

    // Assert
    expect(el.querySelectorAll('path.line').length).toBe(2);
  });

  it('sparkline_ariaLabel_namesFirstAndLast', () => {
    // Act
    const el = render([null, 3.9, 4.2, 5.1]);

    // Assert
    expect(el.querySelector('svg')?.getAttribute('aria-label')).toBe('Score trend: 3.9 to 5.1');
  });

  it('sparkline_shares_labelAsPercent', () => {
    // Act
    const el = render([0.4, 0.6], 1, 'Covered share');

    // Assert
    expect(el.querySelector('svg')?.getAttribute('aria-label')).toBe('Covered share: 40% to 60%');
  });
});

import { TestBed } from '@angular/core/testing';
import { DeltaMetric } from '../../core/api/report';
import { DeltaChip } from './delta-chip';

function render(metric: DeltaMetric | undefined, comparable = true, decimals = 0): HTMLElement {
  const fixture = TestBed.createComponent(DeltaChip);
  fixture.componentRef.setInput('metric', metric);
  fixture.componentRef.setInput('comparable', comparable);
  fixture.componentRef.setInput('decimals', decimals);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const metric = (name: string, change: number | null, direction: DeltaMetric['direction']): DeltaMetric => ({ name, prev: 0, curr: 0, change, direction });

describe('DeltaChip', () => {
  it('chip improved score shows ▲ 0.4 up', () => {
    // Act
    const chip = render(metric('qualityScore', 0.4, 'improved'), true, 1).querySelector('.delta') as HTMLElement;

    // Assert
    expect([chip.textContent?.trim(), chip.classList.contains('up')]).toEqual(['▲ 0.4', true]);
  });

  it('chip fewer untested shows ▼ 1 up', () => {
    // Act
    const chip = render(metric('endpoints.none', -1, 'improved')).querySelector('.delta') as HTMLElement;

    // Assert
    expect([chip.textContent?.trim(), chip.classList.contains('up')]).toEqual(['▼ 1', true]);
  });

  it('chip direction is in text for screen readers', () => {
    // Act
    const el = render(metric('qualityScore', 0.4, 'improved'), true, 1);

    // Assert
    expect(el.querySelector('.sr-only')?.textContent?.trim()).toBe('improved since the previous scan');
  });

  it('chip regression is down', () => {
    // Act
    const chip = render(metric('endpoints.weak', 2, 'regressed')).querySelector('.delta') as HTMLElement;

    // Assert
    expect(chip.classList.contains('down')).toBe(true);
  });

  it('chip not comparable is flat', () => {
    // Act
    const chip = render(metric('qualityScore', 0.4, 'improved'), false, 1).querySelector('.delta') as HTMLElement;

    // Assert
    expect([chip.classList.contains('flat'), chip.classList.contains('up')]).toEqual([true, false]);
  });

  it('chip zero change renders nothing', () => {
    // Act
    const el = render(metric('endpoints.weak', 0, 'unchanged'));

    // Assert
    expect(el.querySelector('.delta')).toBeNull();
  });

  it('chip missing metric renders nothing', () => {
    // Act
    const el = render(undefined);

    // Assert
    expect(el.querySelector('.delta')).toBeNull();
  });
});

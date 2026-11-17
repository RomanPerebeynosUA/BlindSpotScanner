import { TestBed } from '@angular/core/testing';
import { sampleReport } from '../../../testing/sample-report';
import { DeltaChanges } from './delta-changes';

function render(): HTMLElement {
  const fixture = TestBed.createComponent(DeltaChanges);
  fixture.componentRef.setInput('delta', sampleReport().delta);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const panel = (el: HTMLElement, title: string) =>
  [...el.querySelectorAll('.panel')].find((p) => p.querySelector('h3')?.textContent?.trim() === title) as HTMLElement;

describe('DeltaChanges', () => {
  it('changes lists newly covered POST api/orders and Regressed None.', () => {
    // Act
    const el = render();

    // Assert
    expect(panel(el, 'Newly covered').querySelector('li')?.textContent?.trim()).toBe('POST api/orders');
    expect(panel(el, 'Regressed').textContent).toContain('None.');
  });

  it('changes lists dropped and new risks', () => {
    // Act
    const el = render();

    // Assert
    const risks = panel(el, 'Risks').textContent ?? '';
    expect(risks).toContain('Dropped: Nothing tests that orders are really placed and charged');
    expect(risks).toContain('New: Order cancellation is not tested at all');
  });

  it('changes lists fixed and new findings', () => {
    // Act
    const el = render();

    // Assert
    const findings = panel(el, 'Findings').textContent ?? '';
    expect(findings).toContain('Fixed: uncovered-endpoint:POST api/orders');
    expect(findings).toContain('New: uncovered-seam:payment-gateway');
  });
});

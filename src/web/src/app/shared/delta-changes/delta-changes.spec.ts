import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { sampleReport } from '../../../testing/sample-report';
import { RunDelta } from '../../core/api/report';
import { DeltaChanges } from './delta-changes';
import { ChangesDialog } from './changes-dialog/changes-dialog';

const open = vi.fn();

function render(change: (d: RunDelta) => void = () => undefined): HTMLElement {
  open.mockReset();
  TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }] });
  const delta = sampleReport().delta!;
  change(delta);
  const fixture = TestBed.createComponent(DeltaChanges);
  fixture.componentRef.setInput('delta', delta);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const fiveRisks = (d: RunDelta) => {
  d.risks.dropped = ['Risk one'];
  d.risks.new = ['Risk two', 'Risk three', 'Risk four', 'Risk five'];
};

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

  it('card previews at most three items and offers the rest', () => {
    // Act
    const el = render(fiveRisks);

    // Assert
    const risks = panel(el, 'Risks');
    expect({ shown: risks.querySelectorAll('li').length, more: risks.querySelector('button')?.textContent?.trim() }).toEqual({ shown: 3, more: 'Show all 5' });
  });

  it('show all opens every item in a dialog', () => {
    // Arrange
    const el = render(fiveRisks);

    // Act
    (panel(el, 'Risks').querySelector('button') as HTMLButtonElement).click();

    // Assert
    expect(open).toHaveBeenCalledWith(ChangesDialog, expect.objectContaining({
      ariaLabelledBy: 'changes-title',
      width: '720px',
      data: {
        title: 'Risks',
        items: [
          { label: 'Dropped', text: 'Risk one', mono: false },
          { label: 'New', text: 'Risk two', mono: false },
          { label: 'New', text: 'Risk three', mono: false },
          { label: 'New', text: 'Risk four', mono: false },
          { label: 'New', text: 'Risk five', mono: false },
        ],
      },
    }));
  });

  it('an empty card offers no details', () => {
    // Act
    const el = render();

    // Assert
    expect(panel(el, 'Regressed').querySelector('button')).toBeNull();
  });
});

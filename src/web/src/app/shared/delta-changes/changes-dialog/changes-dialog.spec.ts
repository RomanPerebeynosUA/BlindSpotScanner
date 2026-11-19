import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ChangesDialog, ChangesDialogData } from './changes-dialog';

function render(data: ChangesDialogData): HTMLElement {
  TestBed.configureTestingModule({
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close: vi.fn() } },
    ],
  });
  const fixture = TestBed.createComponent(ChangesDialog);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ChangesDialog', () => {
  it('shows the title and every item with its label', () => {
    // Act
    const el = render({ title: 'Risks', items: [{ label: 'Dropped', text: 'Risk one', mono: false }, { text: 'GET api/orders', mono: true }] });

    // Assert
    expect({
      title: el.querySelector('#changes-title')?.textContent?.trim(),
      items: [...el.querySelectorAll('li')].map((li) => li.textContent?.replace(/\s+/g, ' ').trim()),
      mono: el.querySelectorAll('li .mono').length,
    }).toEqual({ title: 'Risks (2)', items: ['Dropped: Risk one', 'GET api/orders'], mono: 1 });
  });
});

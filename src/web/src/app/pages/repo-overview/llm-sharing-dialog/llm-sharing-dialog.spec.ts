import { TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { LlmSharingDialog } from './llm-sharing-dialog';

describe('LlmSharingDialog', () => {
  let close: ReturnType<typeof vi.fn>;

  function render() {
    close = vi.fn();
    TestBed.configureTestingModule({ imports: [LlmSharingDialog], providers: [{ provide: MatDialogRef, useValue: { close } }] });
    const fixture = TestBed.createComponent(LlmSharingDialog);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('explains what is sent and keeps Continue disabled until checked', () => {
    // Act
    const { el } = render();

    // Assert
    const button = el.querySelector('button[type=submit]') as HTMLButtonElement;
    expect([el.textContent?.includes('secret scrubbing'), button.disabled]).toEqual([true, true]);
  });

  it('checking and continuing closes with consent', () => {
    // Arrange
    const { fixture, el } = render();
    const box = el.querySelector('input[type=checkbox]') as HTMLInputElement;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();

    // Assert
    expect(close).toHaveBeenCalledWith(true);
  });
});

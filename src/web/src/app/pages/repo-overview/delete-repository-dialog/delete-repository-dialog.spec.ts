import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { DeleteRepositoryDialog } from './delete-repository-dialog';

describe('DeleteRepositoryDialog', () => {
  let close: ReturnType<typeof vi.fn>;

  function render() {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [DeleteRepositoryDialog],
      providers: [{ provide: MatDialogRef, useValue: { close } }, { provide: MAT_DIALOG_DATA, useValue: { fullName: 'example-org/bookstore' } }],
    });
    const fixture = TestBed.createComponent(DeleteRepositoryDialog);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('names the repository and says what is lost and what is kept', () => {
    // Act
    const el = render();

    // Assert
    const text = el.textContent ?? '';
    expect([text.includes('example-org/bookstore'), text.includes('every scan'), text.includes('Jira tickets and pull requests')]).toEqual([true, true, true]);
  });

  it('Delete closes with confirmation', () => {
    // Arrange
    const el = render();

    // Act
    (el.querySelector('button.danger') as HTMLButtonElement).click();

    // Assert
    expect(close).toHaveBeenCalledWith(true);
  });

  it('Cancel closes without confirmation', () => {
    // Arrange
    const el = render();

    // Act
    (el.querySelector('button.cancel') as HTMLButtonElement).click();

    // Assert
    expect(close).toHaveBeenCalledWith(false);
  });
});

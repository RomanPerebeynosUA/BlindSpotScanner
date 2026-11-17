import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { AddRepositoryDialog } from './add-repository-dialog';

describe('AddRepositoryDialog', () => {
  const navigate = vi.fn();
  const close = vi.fn();
  const add = vi.fn();

  async function render(): Promise<{ el: HTMLElement; detect: () => Promise<void> }> {
    navigate.mockReset();
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [AddRepositoryDialog],
      providers: [
        { provide: RepositoriesApi, useValue: { add } },
        { provide: Router, useValue: { navigate } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(AddRepositoryDialog);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return {
      el,
      detect: async () => {
        fixture.detectChanges();
        await fixture.whenStable();
      },
    };
  }

  function type(el: HTMLElement, selector: string, value: string): void {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('submit adds and navigates to the profile page', async () => {
    // Arrange
    add.mockReturnValue(of({ id: 'r1' }));
    const { el, detect } = await render();
    type(el, 'input[name=url]', 'https://github.com/example-org/bookstore');
    type(el, 'input[name=branch]', 'dev');
    await detect();

    // Act
    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await detect();

    // Assert
    expect(add).toHaveBeenCalledWith('https://github.com/example-org/bookstore', 'dev');
    expect(close).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/repos', 'r1', 'profile']);
  });

  it('409 navigates to the existing repository', async () => {
    // Arrange
    add.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { id: 'existing' } })));
    const { el, detect } = await render();
    type(el, 'input[name=url]', 'https://github.com/example-org/bookstore');
    await detect();

    // Act
    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();
    await detect();

    // Assert
    expect(navigate).toHaveBeenCalledWith(['/repos', 'existing']);
  });

  it('submit disabled while the URL is empty', async () => {
    // Act
    const { el } = await render();

    // Assert
    expect((el.querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
  });
});

import { TestBed } from '@angular/core/testing';
import { StatusChip } from './status-chip';

describe('StatusChip', () => {
  it.each([
    ['covered', '✅ Covered'],
    ['weak', '⚠️ Weak'],
    ['skipped', '⏭️ Skipped'],
    ['none', '❌ None'],
    ['unverified', '❔ Unverified'],
    ['Running', 'Running'],
  ])('shows icon and text for %s', (status, text) => {
    // Arrange
    const fixture = TestBed.createComponent(StatusChip);
    fixture.componentRef.setInput('status', status);

    // Act
    fixture.detectChanges();

    // Assert
    const chip = fixture.nativeElement.querySelector('.chip') as HTMLElement;
    expect(chip.textContent?.trim()).toBe(text);
    expect(chip.classList).toContain(`s-${status.toLowerCase()}`);
  });
});

import { Component, inject, signal } from '@angular/core';
import { MatDialogRef } from '@angular/material/dialog';

/** Doc 10's "LLM data sharing acknowledged": asked once per repository before its first scan. Closes with true on consent. */
@Component({
  selector: 'app-llm-sharing-dialog',
  templateUrl: './llm-sharing-dialog.html',
  styleUrl: './llm-sharing-dialog.scss',
})
export class LlmSharingDialog {
  private readonly dialog = inject<MatDialogRef<LlmSharingDialog, boolean>>(MatDialogRef);

  protected readonly agreed = signal(false);

  protected toggle(event: Event): void {
    this.agreed.set((event.target as HTMLInputElement).checked);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.agreed()) {
      this.dialog.close(true);
    }
  }

  protected cancel(): void {
    this.dialog.close(false);
  }
}

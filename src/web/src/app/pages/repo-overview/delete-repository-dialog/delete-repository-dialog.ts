import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

/** Asks before a repository and all its scans are removed. Closes with true on confirmation. */
@Component({
  selector: 'app-delete-repository-dialog',
  templateUrl: './delete-repository-dialog.html',
  styleUrl: './delete-repository-dialog.scss',
})
export class DeleteRepositoryDialog {
  private readonly dialog = inject<MatDialogRef<DeleteRepositoryDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<{ fullName: string }>(MAT_DIALOG_DATA);

  protected confirm(): void {
    this.dialog.close(true);
  }

  protected cancel(): void {
    this.dialog.close(false);
  }
}

import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogClose } from '@angular/material/dialog';

/** One line of a "Changes since" group: an optional label ("New", "Fixed"…) and the text. */
export interface ChangeItem {
  label?: string;
  text: string;
  /** Endpoints read as code; risk and finding sentences don't. */
  mono: boolean;
}

export interface ChangesDialogData {
  title: string;
  items: ChangeItem[];
}

/** Every item of one "Changes since" card, in full; the card itself shows only a preview. */
@Component({
  selector: 'app-changes-dialog',
  imports: [MatDialogClose],
  templateUrl: './changes-dialog.html',
  styleUrl: './changes-dialog.scss',
})
export class ChangesDialog {
  protected readonly data = inject<ChangesDialogData>(MAT_DIALOG_DATA);
}

import { Component, computed, inject, input } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { RunDelta } from '../../core/api/report';
import { ChangeItem, ChangesDialog, ChangesDialogData } from './changes-dialog/changes-dialog';

/** Items a card shows before "Show all" (the dialog lists the rest). */
const PREVIEW = 3;

interface ChangeGroup {
  title: string;
  items: ChangeItem[];
}

const items = (texts: readonly string[] | undefined, mono: boolean, label?: string): ChangeItem[] =>
  (texts ?? []).map((text) => (label ? { label, text, mono } : { text, mono }));

/**
 * What changed between two runs (doc 08 "Changes since"), straight from the backend's delta. Each
 * card previews its first items; clicking it opens every item in a dialog.
 */
@Component({
  selector: 'app-delta-changes',
  templateUrl: './delta-changes.html',
  styleUrl: './delta-changes.scss',
})
export class DeltaChanges {
  private readonly dialog = inject(MatDialog);

  readonly delta = input.required<RunDelta>();

  protected readonly preview = PREVIEW;
  protected readonly groups = computed<ChangeGroup[]>(() => {
    const d = this.delta();
    return [
      { title: 'Newly covered', items: items(d.endpoints.newlyCovered, true) },
      { title: 'Regressed', items: items(d.endpoints.regressed, true) },
      { title: 'Endpoints added / removed', items: [...items(d.endpoints.added, true, 'Added'), ...items(d.endpoints.removed, true, 'Removed')] },
      { title: 'Confidence changed', items: items(d.endpoints.confidenceChanged, true) },
      { title: 'Findings', items: [...items(d.findings.fixed, false, 'Fixed'), ...items(d.findings.new, false, 'New')] },
      { title: 'Risks', items: [...items(d.risks.dropped, false, 'Dropped'), ...items(d.risks.new, false, 'New')] },
    ];
  });

  protected open(group: ChangeGroup): void {
    this.dialog.open<ChangesDialog, ChangesDialogData>(ChangesDialog, {
      data: { title: group.title, items: group.items },
      // A set width: sized to its content, a long unbroken name widened the dialog past the content, leaving an empty strip.
      width: '720px',
      maxWidth: 'calc(100vw - 32px)',
      ariaLabelledBy: 'changes-title',
    });
  }
}

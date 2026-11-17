import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { RunsApi } from '../../core/api/runs-api';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { Comparison, RunHistoryItem } from '../../core/api/api-types';
import { DeltaDirection } from '../../core/api/report';
import { DeltaChanges } from '../../shared/delta-changes/delta-changes';

const METRIC_LABELS: Record<string, string> = {
  qualityScore: 'Quality score',
  'endpoints.covered': 'Covered operations',
  'endpoints.weak': 'Weak',
  'endpoints.none': 'Untested',
  'endpoints.unverified': 'Unverified',
  falsePositiveCount: 'False positives',
};

/**
 * `/compare?base=&target=`: any two runs of one repository (doc 08). The delta comes from the API
 * (stored or ad-hoc); the page only renders it, uncoloured when the runs aren't comparable.
 */
@Component({
  selector: 'app-compare',
  imports: [DatePipe, DeltaChanges],
  templateUrl: './compare.html',
  styleUrl: './compare.scss',
})
export class Compare {
  private readonly runs = inject(RunsApi);
  private readonly repositories = inject(RepositoriesApi);
  private readonly router = inject(Router);

  readonly base = input<string | undefined>();
  readonly target = input<string | undefined>();

  protected readonly comparison = signal<Comparison | null>(null);
  protected readonly failed = signal(false);
  protected readonly repositoryName = signal('');
  protected readonly choices = signal<RunHistoryItem[]>([]);

  constructor() {
    effect(() => {
      const [base, target] = [this.base(), this.target()];
      untracked(() => this.load(base, target));
    });
  }

  protected label(name: string): string {
    return METRIC_LABELS[name] ?? name;
  }

  protected value(name: string, v: number | null): string {
    return v === null ? 'N/A' : name === 'qualityScore' ? v.toFixed(1) : String(v);
  }

  /** "▲ 0.4 improved" / "▼ 1 improved" / "unchanged"; the direction word only when the runs are comparable. */
  protected change(name: string, change: number | null, direction: DeltaDirection | null, comparable: boolean): string {
    if (change === null || change === 0) {
      return 'unchanged';
    }

    const amount = `${change > 0 ? '▲' : '▼'} ${Math.abs(change).toFixed(name === 'qualityScore' ? 1 : 0)}`;
    return comparable && direction ? `${amount} ${direction}` : amount;
  }

  protected tone(direction: DeltaDirection, comparable: boolean): string {
    return !comparable || direction === 'unchanged' ? '' : direction === 'improved' ? 'up' : 'down';
  }

  protected pick(which: 'base' | 'target', event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const queryParams = { base: this.base(), target: this.target(), [which]: value };
    void this.router.navigate(['/compare'], { queryParams });
  }

  private load(base: string | undefined, target: string | undefined): void {
    this.comparison.set(null);
    this.failed.set(false);
    if (!base || !target) {
      return;
    }

    this.runs.compare(base, target).subscribe({
      next: (c) => this.comparison.set(c),
      error: () => this.failed.set(true),
    });
    this.runs.repository(target).subscribe({
      next: (repository) => {
        this.repositoryName.set(repository.fullName);
        this.repositories.runs(repository.id).subscribe((history) => this.choices.set(history.filter((r) => r.status === 'Succeeded')));
      },
      error: () => this.choices.set([]),
    });
  }
}

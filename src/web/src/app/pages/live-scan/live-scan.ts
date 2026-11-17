import { Component, computed, DestroyRef, effect, inject, input, OnInit, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RunsApi } from '../../core/api/runs-api';
import { isReport, RunBody, RunStatusBody, ScanStep } from '../../core/api/api-types';
import { ScanProgressService, ScanWatch } from '../../core/scan-progress';

type PhaseState = 'done' | 'running' | 'failed' | 'waiting';

/** The pipeline's phases (ScanPhases.All) with the prototype's labels. */
const PHASES: readonly { id: string; label: string }[] = [
  { id: 'P1', label: 'P1 Resolve' },
  { id: 'P2', label: 'P2 Enumerate' },
  { id: 'P3', label: 'P3 Endpoint extraction' },
  { id: 'P4', label: 'P4 Test classification' },
  { id: 'P4b', label: 'P4b LLM classification' },
  { id: 'P5', label: 'P5 Coverage mapping' },
  { id: 'P6', label: 'P6 Quality grading' },
  { id: 'P7', label: 'P7 Assemble · validate · verify citations' },
];

const POLL_MS = 5000;

/** `/runs/:runId/live`: the phase stepper, activity log and Cancel (doc 04). */
@Component({
  selector: 'app-live-scan',
  imports: [DatePipe],
  templateUrl: './live-scan.html',
  styleUrl: './live-scan.scss',
})
export class LiveScan implements OnInit {
  private readonly runs = inject(RunsApi);
  private readonly progress = inject(ScanProgressService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly runId = input.required<string>();

  protected readonly phases = PHASES;
  protected readonly steps = signal<ScanStep[]>([]);
  protected readonly watch = signal<ScanWatch | null>(null);
  protected readonly outcome = signal<RunStatusBody | null>(null);
  protected readonly cancelling = signal(false);

  protected readonly events = computed(() => this.watch()?.events() ?? []);
  protected readonly pct = computed(() => this.events().at(-1)?.pct ?? 0);

  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    effect(() => {
      const status = this.watch()?.completed();
      if (status) {
        untracked(() => this.refresh());
      }
    });
    this.destroyRef.onDestroy(() => this.stopWatching());
  }

  ngOnInit(): void {
    // Every read ends with the page: a late answer must not start watching or navigate after the user left.
    this.runs.get(this.runId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe((body) => {
      if (this.handle(body)) {
        return;
      }

      this.runs.steps(this.runId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe((steps) => this.steps.set(steps));
      this.watch.set(this.progress.watch(this.runId()));
      this.timer = setInterval(() => this.refresh(), POLL_MS);
    });
  }

  protected state(phase: string): PhaseState {
    const step = this.steps().find((s) => s.phase === phase);
    const messages = this.events().filter((e) => e.phase === phase).map((e) => e.message);
    if (step?.status === 'Failed') {
      return 'failed';
    }

    if (step?.status === 'Succeeded' || messages.includes(`${phase} done`)) {
      return 'done';
    }

    return messages.includes(`${phase} started`) || step?.status === 'Running' ? 'running' : 'waiting';
  }

  protected notes(phase: string): string | null {
    return this.steps().find((s) => s.phase === phase)?.notes ?? null;
  }

  protected cancel(): void {
    this.cancelling.set(true);
    this.runs.cancel(this.runId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => this.cancelling.set(false) });
  }

  /** Re-reads the run (hub completion or the D21 poll). */
  private refresh(): void {
    this.runs.get(this.runId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe((body) => this.handle(body));
  }

  /** True when the run is finished: a report opens the report page; a failure shows its reasons. */
  private handle(body: RunBody): boolean {
    if (isReport(body)) {
      this.stopWatching();
      void this.router.navigate(['/runs', this.runId()], { replaceUrl: true });
      return true;
    }

    if (body.status === 'Failed' || body.status === 'Cancelled') {
      this.stopWatching();
      this.outcome.set(body);
      return true;
    }

    return false;
  }

  private stopWatching(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }

    const watch = this.watch();
    if (watch) {
      void watch.stop();
    }
  }
}

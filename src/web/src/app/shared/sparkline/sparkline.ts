import { Component, computed, input } from '@angular/core';

const WIDTH = 120;
const HEIGHT = 34;
const PAD = 6;

interface Point {
  x: number;
  y: number;
}

const round = (n: number): number => Number(n.toFixed(1));

/**
 * A tiny trend line (D23: hand-drawn SVG, as in the prototype's `spark()`). A null value is a gap,
 * never 0; the last value gets a dot, and so does a value standing alone between gaps (it has no line). Values are drawn against 0…`max` (10 for scores, 1 for shares).
 */
@Component({
  selector: 'app-sparkline',
  templateUrl: './sparkline.html',
})
export class Sparkline {
  readonly values = input.required<(number | null)[]>();
  readonly max = input(10);
  readonly label = input.required<string>();

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;

  /** Points per unbroken run of values; a null starts a new segment. */
  private readonly segments = computed<Point[][]>(() => {
    const values = this.values();
    const step = values.length > 1 ? (WIDTH - 2 * PAD) / (values.length - 1) : 0;
    const segments: Point[][] = [[]];
    values.forEach((v, i) => {
      if (v === null) {
        segments.push([]);
        return;
      }

      const x = values.length > 1 ? PAD + i * step : WIDTH - PAD;
      segments[segments.length - 1].push({ x: round(x), y: round(HEIGHT - 4 - (v / this.max()) * (HEIGHT - 8)) });
    });
    return segments.filter((s) => s.length > 0);
  });

  protected readonly lines = computed(() =>
    this.segments()
      .filter((s) => s.length > 1)
      .map((s) => ({
        line: s.map((p, i) => `${i ? 'L' : 'M'}${p.x} ${p.y}`).join(' '),
        area: `${s.map((p, i) => `${i ? 'L' : 'M'}${p.x} ${p.y}`).join(' ')} L${s[s.length - 1].x} ${HEIGHT} L${s[0].x} ${HEIGHT} Z`,
      })),
  );

  protected readonly last = computed<Point | null>(() => this.segments().at(-1)?.at(-1) ?? null);

  /** Lone values before the last segment: without a dot they would not be drawn at all. */
  protected readonly isolated = computed<Point[]>(() => this.segments().slice(0, -1).filter((s) => s.length === 1).map((s) => s[0]));

  protected readonly ariaLabel = computed(() => {
    const present = this.values().filter((v): v is number => v !== null);
    const format = (v: number) => (this.max() === 1 ? `${Math.round(v * 100)}%` : v.toFixed(1));
    return `${this.label()}: ${format(present[0])} to ${format(present[present.length - 1])}`;
  });
}

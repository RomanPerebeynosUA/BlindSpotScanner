import { Component, DOCUMENT, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

type Theme = 'light' | 'dark';

const THEME_KEY = 'bss-theme';

/** The app shell: a nav rail (a top bar at ≤800px) and the routed page. */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly document = inject(DOCUMENT);

  constructor() {
    const saved = readTheme();
    if (saved) {
      this.document.documentElement.setAttribute('data-theme', saved);
    }
  }

  protected toggleTheme(): void {
    const html = this.document.documentElement;
    const current = (html.getAttribute('data-theme') as Theme | null) ?? (this.prefersDark() ? 'dark' : 'light');
    const next: Theme = current === 'dark' ? 'light' : 'dark';
    html.setAttribute('data-theme', next);
    try {
      localStorage.setItem(THEME_KEY, next);
    } catch {
      // Storage can be unavailable (private mode); the theme still applies for this visit.
    }
  }

  private prefersDark(): boolean {
    return this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }
}

function readTheme(): Theme | null {
  try {
    const value = localStorage.getItem(THEME_KEY);
    return value === 'light' || value === 'dark' ? value : null;
  } catch {
    return null;
  }
}

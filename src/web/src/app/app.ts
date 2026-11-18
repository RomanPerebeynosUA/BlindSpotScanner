import { Component, computed, DOCUMENT, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth';

type Theme = 'light' | 'dark';

const THEME_KEY = 'bss-theme';

/** "Dev Example" → "DE", "local-admin" → "LA", "dev@example.com" → "DE". */
export function initials(name: string): string {
  const parts = name.split('@')[0].split(/[\s._-]+/).filter((p) => p.length > 0);
  const letters = parts.length >= 2 ? parts[0][0] + parts[1][0] : (parts[0] ?? '?').slice(0, 2);
  return letters.toUpperCase();
}

/**
 * The app shell: a nav rail (a top bar at ≤800px) with the account card pinned to its foot, and the
 * routed page. Signed-out users see the sign-in card; an unreachable server, a retry (D26).
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly document = inject(DOCUMENT);
  protected readonly auth = inject(AuthService);

  protected readonly theme = signal<Theme>('light');

  /** The built-in admin reads as a person, not an account id. */
  protected readonly displayName = computed(() => {
    const me = this.auth.me();
    return !me ? '' : me.localMode && me.name === 'local-admin' ? 'Local admin' : me.name;
  });

  protected readonly avatar = computed(() => initials(this.displayName() || '?'));

  protected readonly role = computed(() => this.auth.me()?.roles[0] ?? '');

  constructor() {
    const saved = readTheme();
    if (saved) {
      this.document.documentElement.setAttribute('data-theme', saved);
    }

    this.theme.set(this.current());
  }

  protected toggleTheme(): void {
    const next: Theme = this.current() === 'dark' ? 'light' : 'dark';
    this.document.documentElement.setAttribute('data-theme', next);
    this.theme.set(next);
    try {
      localStorage.setItem(THEME_KEY, next);
    } catch {
      // Storage can be unavailable (private mode); the theme still applies for this visit.
    }
  }

  private current(): Theme {
    const set = this.document.documentElement.getAttribute('data-theme') as Theme | null;
    return set ?? (this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
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

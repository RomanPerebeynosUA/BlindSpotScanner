import { HttpClient } from '@angular/common/http';
import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { catchError, firstValueFrom, of } from 'rxjs';

export type Role = 'Viewer' | 'Member' | 'Admin';

/** GET /api/me (D26). */
export interface Me {
  name: string;
  roles: string[];
}

/** Lowest to highest; a higher role includes the lower ones (doc 10). */
const ROLE_ORDER: readonly Role[] = ['Viewer', 'Member', 'Admin'];

/** Whether `me` holds `role` or a higher one. */
export function hasRole(me: Me | null | undefined, role: Role): boolean {
  const needed = ROLE_ORDER.indexOf(role);
  return !!me && me.roles.some((r) => ROLE_ORDER.indexOf(r as Role) >= needed);
}

/** The browser location, injectable so sign-in redirects can be tested. */
export const BROWSER_LOCATION = new InjectionToken<Pick<Location, 'href' | 'pathname' | 'search'>>('BROWSER_LOCATION', {
  providedIn: 'root',
  factory: () => window.location,
});

/** The server-side sign-in (D26), returning to the current page. */
export function signInUrl(location: Pick<Location, 'pathname' | 'search'>): string {
  return `/api/auth/login?returnUrl=${encodeURIComponent(location.pathname + location.search)}`;
}

/**
 * Who is signed in (D26). The API owns the session (an HttpClient-free cookie), so the app only reads
 * `/api/me` once at start-up and hides what the user's role can't do; the API enforces it anyway.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly location = inject(BROWSER_LOCATION);

  /** undefined while loading, null when signed out. */
  readonly me = signal<Me | null | undefined>(undefined);

  load(): Promise<void> {
    return firstValueFrom(this.http.get<Me>('/api/me').pipe(catchError(() => of(null)))).then((me) => this.me.set(me));
  }

  can(role: Role): boolean {
    return hasRole(this.me(), role);
  }

  login(): void {
    this.location.href = signInUrl(this.location);
  }
}

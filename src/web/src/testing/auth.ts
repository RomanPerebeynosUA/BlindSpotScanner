import { signal } from '@angular/core';
import { AuthService, hasRole, Me, Role } from '../app/core/auth/auth';

/** An AuthService stand-in signed in with `role` (null = anonymous), for component specs. */
export function provideTestAuth(role: Role | null = 'Admin', user: Partial<Me> = {}) {
  const me = signal<Me | null | undefined>(role ? { name: 'test-user', roles: [role], localMode: false, ...user } : null);
  return {
    provide: AuthService,
    useValue: { me, unreachable: signal(false), can: (r: Role) => hasRole(me(), r), login: vi.fn(), load: () => Promise.resolve() } as unknown as AuthService,
  };
}

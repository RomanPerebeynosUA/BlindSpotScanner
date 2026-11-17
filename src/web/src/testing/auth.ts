import { signal } from '@angular/core';
import { AuthService, hasRole, Me, Role } from '../app/core/auth/auth';

/** An AuthService stand-in signed in with `role` (null = anonymous), for component specs. */
export function provideTestAuth(role: Role | null = 'Admin') {
  const me = signal<Me | null>(role ? { name: 'test-user', roles: [role] } : null);
  return {
    provide: AuthService,
    useValue: { me, can: (r: Role) => hasRole(me(), r), login: vi.fn(), load: () => Promise.resolve() } as unknown as AuthService,
  };
}

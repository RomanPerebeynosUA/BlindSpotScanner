import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/repositories/repositories').then((m) => m.Repositories) },
  { path: 'repos/:id', loadComponent: () => import('./pages/repo-overview/repo-overview').then((m) => m.RepoOverview) },
  { path: 'repos/:id/profile', loadComponent: () => import('./pages/profile-editor/profile-editor').then((m) => m.ProfileEditor) },
  { path: '**', redirectTo: '' },
];

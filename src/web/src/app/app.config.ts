import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { MAT_DIALOG_DEFAULT_OPTIONS, MatDialogConfig } from '@angular/material/dialog';
import { routes } from './app.routes';
import { problemInterceptor } from './core/problem-interceptor';
import { AuthService } from './core/auth/auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([problemInterceptor])),
    // Who is signed in, before the first page renders (D26).
    provideAppInitializer(() => inject(AuthService).load()),
    // A dialog taller than the window scrolls inside itself instead of hiding its buttons off-screen.
    { provide: MAT_DIALOG_DEFAULT_OPTIONS, useValue: { ...new MatDialogConfig(), maxHeight: 'calc(100vh - 32px)' } },
  ],
};

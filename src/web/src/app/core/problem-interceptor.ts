import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';

interface ProblemDetails {
  title?: string;
  detail?: string;
}

/** Shows an API error (ProblemDetails) in a snackbar and rethrows it, so callers still decide what to do. */
export const problemInterceptor: HttpInterceptorFn = (request, next) => {
  const snackBar = inject(MatSnackBar);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        snackBar.open(message(error), 'Dismiss', { duration: 6000 });
      }

      return throwError(() => error);
    }),
  );
};

function message(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return "Can't reach the server";
  }

  const problem = (typeof error.error === 'object' && error.error !== null ? error.error : {}) as ProblemDetails;
  return problem.detail ?? problem.title ?? `Request failed (${error.status})`;
}

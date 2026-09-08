import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails, isInsufficientFunds, isProblemDetails } from './models';

/**
 * Maps RFC 7807 ProblemDetails responses to a MatSnackBar message.
 * Insufficient-funds errors are skipped so the transfer form can render them
 * inline; every error is rethrown for callers to react to.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        const problem = isProblemDetails(error.error) ? error.error : null;
        if (!isInsufficientFunds(problem)) {
          snackBar.open(messageFor(error, problem), 'Fechar');
        }
      }
      return throwError(() => error);
    }),
  );
};

function messageFor(error: HttpErrorResponse, problem: ProblemDetails | null): string {
  if (error.status === 0) {
    return 'Não foi possível conectar ao servidor.';
  }
  return problem?.detail ?? problem?.title ?? `Erro inesperado (HTTP ${error.status}).`;
}

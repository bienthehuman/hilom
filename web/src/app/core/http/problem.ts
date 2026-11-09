import { HttpErrorResponse } from '@angular/common/http';

/** An API error in RFC 7807 ProblemDetails form, with Hilom's stable `code`. */
export interface Problem {
  status: number;
  code?: string;
  title?: string;
  detail?: string;
  /** Validation messages per field, keyed by the camelCase field name. */
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}

/** Turns anything thrown by an API call into a Problem the UI can show. */
export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return {
        status: 0,
        code: 'network_error',
        detail: "Can't reach the server. Check your connection and try again.",
      };
    }
    const body =
      typeof error.error === 'object' && error.error !== null
        ? (error.error as Partial<Problem>)
        : {};
    return { ...body, status: error.status };
  }
  return { status: 0, code: 'unexpected', detail: 'Something went wrong. Please try again.' };
}

export function problemCode(error: unknown): string | undefined {
  return error instanceof HttpErrorResponse ? toProblem(error).code : undefined;
}

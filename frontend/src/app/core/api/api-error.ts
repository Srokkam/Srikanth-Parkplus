import { HttpErrorResponse } from '@angular/common/http';
import { FailedLine } from '../models';

export interface ProblemBody {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}

/** What the API (or the network) said went wrong, in one typed shape. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly extensions: Record<string, unknown> = {},
    readonly fieldErrors: Record<string, string[]> = {},
    readonly retryAfterSeconds: number | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
  }

  /** Worth trying again with the same ids: the request may or may not have been applied. */
  get retryable(): boolean {
    return this.status === 0 || this.status === 429 || this.status >= 500;
  }

  get failedLines(): FailedLine[] {
    const lines = this.extensions['failedLines'];
    return Array.isArray(lines) ? (lines as FailedLine[]) : [];
  }

  get currentTotalPaise(): number | null {
    const total = this.extensions['currentTotalPaise'];
    return typeof total === 'number' ? total : null;
  }

  static network(): ApiError {
    return new ApiError(0, 'network', 'We could not reach the server. Check your connection and try again.');
  }

  static fromHttp(response: HttpErrorResponse): ApiError {
    if (response.status === 0) {
      return ApiError.network();
    }

    const body: ProblemBody = typeof response.error === 'object' && response.error !== null ? response.error : {};
    const { type, title, status, detail, code, errors, ...extensions } = body;
    const retryAfter = Number(response.headers?.get('Retry-After'));

    return new ApiError(
      response.status,
      code ?? `http-${response.status}`,
      detail ?? title ?? `The request failed (${response.status}).`,
      extensions,
      errors ?? {},
      Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null,
    );
  }
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  return new ApiError(500, 'unexpected', 'Something unexpected went wrong.');
}

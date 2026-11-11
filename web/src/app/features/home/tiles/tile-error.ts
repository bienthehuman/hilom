import { toProblem } from '../../../core/http/problem';

/** The message for a tile whose data failed to load, or null when nothing failed. */
export function tileError(error: unknown): string | null {
  return error ? (toProblem(error).detail ?? 'Please try again.') : null;
}

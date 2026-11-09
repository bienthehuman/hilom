import { type ValidationErrors } from '@angular/forms';

type MessageFactory = (detail: unknown, label: string) => string;

const lengthOf = (detail: unknown, key: 'requiredLength'): number =>
  typeof detail === 'object' && detail !== null && key in detail
    ? Number((detail as Record<string, unknown>)[key])
    : 0;

const boundOf = (detail: unknown, key: 'min' | 'max'): unknown =>
  typeof detail === 'object' && detail !== null ? (detail as Record<string, unknown>)[key] : '';

/**
 * Plain-language messages for Angular's built-in validators. Says what is wrong and what to do.
 * Custom validators can return `{ message: '…' }` to supply their own text.
 */
const MESSAGES: Record<string, MessageFactory> = {
  required: (_, label) => `${label} is required.`,
  email: () => 'Enter an email address like name@example.com.',
  minlength: (d) => `Use at least ${lengthOf(d, 'requiredLength')} characters.`,
  maxlength: (d) => `Use at most ${lengthOf(d, 'requiredLength')} characters.`,
  min: (d) => `Enter ${String(boundOf(d, 'min'))} or more.`,
  max: (d) => `Enter ${String(boundOf(d, 'max'))} or less.`,
  pattern: () => 'This format is not valid.',
  server: (d) => String(d),
};

/** The first error message for a control, or null when it is valid. */
export function firstErrorMessage(errors: ValidationErrors | null, label: string): string | null {
  if (!errors) {
    return null;
  }

  const [key, detail] = Object.entries(errors)[0] ?? [];
  if (key === undefined) {
    return null;
  }

  if (typeof detail === 'object' && detail !== null && 'message' in detail) {
    return String((detail as { message: unknown }).message);
  }

  return MESSAGES[key]?.(detail, label) ?? `${label} is not valid.`;
}

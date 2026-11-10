/**
 * The single source of truth for how every status in Hilom looks (docs/DESIGN_SYSTEM.md §3).
 * A status means the same thing, and has the same color, on every screen.
 */

export type StatusTone = 'info' | 'primary' | 'success' | 'warning' | 'danger' | 'neutral';

export interface StatusAppearance {
  label: string;
  tone: StatusTone;
}

export const STATUS_MAP = {
  appointment: {
    requested: { label: 'Requested', tone: 'info' },
    confirmed: { label: 'Confirmed', tone: 'primary' },
    checked_in: { label: 'Checked in', tone: 'primary' },
    completed: { label: 'Completed', tone: 'success' },
    no_show: { label: 'No show', tone: 'warning' },
    declined: { label: 'Declined', tone: 'danger' },
    cancelled: { label: 'Cancelled', tone: 'neutral' },
  },
  swap: {
    pending_target: { label: 'Awaiting colleague', tone: 'warning' },
    pending_approval: { label: 'Awaiting approval', tone: 'warning' },
    approved: { label: 'Approved', tone: 'success' },
    declined_by_target: { label: 'Declined', tone: 'danger' },
    rejected: { label: 'Rejected', tone: 'danger' },
    cancelled: { label: 'Cancelled', tone: 'neutral' },
  },
  order: {
    active: { label: 'Active', tone: 'primary' },
    completed: { label: 'Completed', tone: 'success' },
    discontinued: { label: 'Discontinued', tone: 'neutral' },
  },
  patientLink: {
    pending: { label: 'Pending verification', tone: 'warning' },
    active: { label: 'Active', tone: 'success' },
    rejected: { label: 'Rejected', tone: 'danger' },
    revoked: { label: 'Revoked', tone: 'neutral' },
  },
  encounter: {
    open: { label: 'Open', tone: 'primary' },
    closed: { label: 'Closed', tone: 'neutral' },
    voided: { label: 'Voided', tone: 'neutral' },
  },
  medicationOutcome: {
    given: { label: 'Given', tone: 'success' },
    held: { label: 'Held', tone: 'warning' },
    not_available: { label: 'Not available', tone: 'warning' },
    refused: { label: 'Refused', tone: 'danger' },
  },
  user: {
    active: { label: 'Active', tone: 'success' },
    locked: { label: 'Locked', tone: 'warning' },
    disabled: { label: 'Disabled', tone: 'neutral' },
  },
  lookup: {
    active: { label: 'Active', tone: 'success' },
    inactive: { label: 'Inactive', tone: 'neutral' },
  },
  employee: {
    active: { label: 'Active', tone: 'success' },
    on_leave: { label: 'On leave', tone: 'warning' },
    separated: { label: 'Separated', tone: 'neutral' },
  },
  auditOutcome: {
    success: { label: 'Succeeded', tone: 'success' },
    failed: { label: 'Failed', tone: 'warning' },
    denied: { label: 'Denied', tone: 'danger' },
  },
  auditSeverity: {
    info: { label: 'Info', tone: 'neutral' },
    notice: { label: 'Notice', tone: 'info' },
    critical: { label: 'Critical', tone: 'danger' },
  },
} as const satisfies Record<string, Record<string, StatusAppearance>>;

export type StatusKind = keyof typeof STATUS_MAP;
export type StatusOf<K extends StatusKind> = keyof (typeof STATUS_MAP)[K] & string;

/** Looks up a status. Unknown values (e.g. a new server status) fall back to a neutral pill with the raw value. */
export function statusAppearance(kind: StatusKind, status: string): StatusAppearance {
  const entries: Record<string, StatusAppearance> = STATUS_MAP[kind];
  return entries[status] ?? { label: status.replaceAll('_', ' '), tone: 'neutral' };
}

/** Pill colors per tone: soft background with the meaning color as text (all AA in both themes). */
export const STATUS_TONE_CLASSES: Record<StatusTone, string> = {
  info: 'bg-info-soft text-info',
  primary: 'bg-accent text-accent-foreground',
  success: 'bg-success-soft text-success',
  warning: 'bg-warning-soft text-warning',
  danger: 'bg-destructive-soft text-destructive',
  neutral: 'bg-muted text-muted-foreground',
};

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  STATUS_MAP,
  STATUS_TONE_CLASSES,
  type StatusAppearance,
  type StatusKind,
  statusAppearance,
} from './status-map';
import { StatusPill } from './status-pill';

describe('status map', () => {
  it('gives every status a label and a known tone', () => {
    for (const statuses of Object.values(STATUS_MAP)) {
      for (const appearance of Object.values(statuses) as StatusAppearance[]) {
        expect(appearance.label.length).toBeGreaterThan(0);
        expect(STATUS_TONE_CLASSES[appearance.tone]).toBeDefined();
      }
    }
  });

  it('keeps the shared meaning of colors', () => {
    expect(statusAppearance('appointment', 'declined').tone).toBe('danger');
    expect(statusAppearance('medicationOutcome', 'given').tone).toBe('success');
    expect(statusAppearance('swap', 'pending_approval').tone).toBe('warning');
    expect(statusAppearance('auditSeverity', 'critical').tone).toBe('danger');
  });

  it('falls back to a readable neutral pill for statuses it does not know', () => {
    expect(statusAppearance('order', 'on_hold')).toEqual({ label: 'on hold', tone: 'neutral' });
  });
});

@Component({
  imports: [StatusPill],
  template: `<ui-status-pill [kind]="kind" [status]="status" />`,
})
class Host {
  kind: StatusKind = 'appointment';
  status = 'confirmed';
}

describe('StatusPill', () => {
  it('shows the label with its tone', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const pill = fixture.nativeElement.querySelector('ui-status-pill') as HTMLElement;

    expect(pill.textContent?.trim()).toBe('Confirmed');
    expect(pill.dataset['tone']).toBe('primary');
    expect(pill.className).toContain('bg-accent');
  });
});

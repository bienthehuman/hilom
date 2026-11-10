import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ShiftChip } from './shift-chip';
import { formatShiftHours, formatShiftLength } from './shift-time';

@Component({
  imports: [ShiftChip],
  template: `<ui-shift-chip code="NOC" color="noc" name="Night" detail="22:00–06:00" />`,
})
class Host {}

describe('ShiftChip', () => {
  it('shows the code, a colored dot and the name for screen readers', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const chip = fixture.nativeElement.querySelector('ui-shift-chip') as HTMLElement;

    expect(chip.textContent?.replace(/\s+/g, ' ').trim()).toBe('NOC (Night) 22:00–06:00');
    expect(chip.querySelector('.bg-shift-noc')).not.toBeNull();
    expect(chip.getAttribute('title')).toBe('Night');
  });
});

describe('shift times', () => {
  it('formats hours, marking shifts that end the next day', () => {
    expect(formatShiftHours('06:00:00', '14:00:00', false)).toBe('06:00–14:00');
    expect(formatShiftHours('22:00:00', '06:00:00', true)).toBe('22:00–06:00 next day');
  });

  it('formats lengths in hours and minutes', () => {
    expect(formatShiftLength(480)).toBe('8 h');
    expect(formatShiftLength(450)).toBe('7 h 30 min');
    expect(formatShiftLength(45)).toBe('45 min');
  });
});

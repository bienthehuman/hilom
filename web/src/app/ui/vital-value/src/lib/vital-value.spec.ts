import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { flagText, VitalValue, worstFlag } from './vital-value';

function render(value: string, flag: string | null) {
  const fixture = TestBed.createComponent(VitalValue);
  fixture.componentRef.setInput('value', value);
  fixture.componentRef.setInput('unit', 'bpm');
  fixture.componentRef.setInput('flag', flag);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('vital-value', () => {
  it('shows a normal reading plainly', () => {
    const el = render('72', 'normal');
    expect(el.textContent?.trim()).toBe('72bpm');
    expect(el.classList.contains('text-warning')).toBe(false);
    expect(el.querySelector('ng-icon')).toBeNull();
  });

  it('marks out-of-range readings with an arrow and spoken direction', () => {
    const el = render('110', 'high');
    expect(el.classList.contains('text-warning')).toBe(true);
    expect(el.querySelector('.sr-only')?.textContent).toBe('(high)');
  });

  it('makes critical readings destructive and bold', () => {
    const el = render('35', 'critical_low');
    expect(el.classList.contains('text-destructive')).toBe(true);
    expect(el.classList.contains('font-semibold')).toBe(true);
    expect(el.querySelector('.sr-only')?.textContent).toBe('(critically low)');
  });

  it('picks the more serious flag', () => {
    expect(worstFlag('high', 'critical_high')).toBe('critical_high');
    expect(worstFlag('normal', null, 'low')).toBe('low');
    expect(worstFlag(null, undefined)).toBeNull();
    expect(flagText('critical_high')).toBe('critically high');
  });
});

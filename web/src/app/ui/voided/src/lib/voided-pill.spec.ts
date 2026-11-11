import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { VoidedPill } from './voided-pill';

describe('voided-pill', () => {
  it('says voided and reads out the reason and who voided it', () => {
    const fixture = TestBed.createComponent(VoidedPill);
    fixture.componentRef.setInput('reason', 'Wrong patient');
    fixture.componentRef.setInput('by', 'Ana Reyes, 11 Nov 14:05');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const pill = el.querySelector('[data-tone="neutral"]')!;
    expect(pill.textContent).toContain('Voided');
    expect(pill.getAttribute('tabindex')).toBe('0');
    expect(el.querySelector('.sr-only')?.textContent).toBe(
      ': Wrong patient (Ana Reyes, 11 Nov 14:05)',
    );
  });
});

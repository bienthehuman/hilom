import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { formatAge } from './patient-age';
import { PatientHeader, type PatientIdentity } from './patient-header';

const JUAN: PatientIdentity = {
  mrn: 'MRN-000123',
  firstName: 'Juan',
  middleName: 'Santos',
  lastName: 'Dela Cruz',
  birthDate: '1958-03-21',
  sex: 'male',
};

@Component({
  imports: [PatientHeader],
  template: `<ui-patient-header [patient]="patient" today="2026-11-10" [location]="location()" />`,
})
class Host {
  readonly patient = JUAN;
  readonly location = signal<string | null>(null);
}

describe('PatientHeader', () => {
  it('shows the name as the heading, then MRN, age and sex', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const header = fixture.nativeElement as HTMLElement;

    expect(header.querySelector('h1')?.textContent?.trim()).toBe('Dela Cruz, Juan Santos');
    const facts = [...header.querySelectorAll('dd')].map((dd) => dd.textContent?.trim());
    expect(facts).toEqual(['MRN-000123', '68 y · Male']);
  });

  it('shows the ward and bed when given', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.location.set('Ward 3 · Bed 12');
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Ward 3 · Bed 12');
  });
});

describe('formatAge', () => {
  it('counts whole years, only after the birthday', () => {
    expect(formatAge('1958-03-21', '2026-03-20')).toBe('67 y');
    expect(formatAge('1958-03-21', '2026-03-21')).toBe('68 y');
  });

  it('uses months under two years and days under a month', () => {
    expect(formatAge('2025-05-10', '2026-11-10')).toBe('18 mo');
    expect(formatAge('2024-11-11', '2026-11-10')).toBe('23 mo');
    expect(formatAge('2026-10-10', '2026-11-10')).toBe('1 mo');
    expect(formatAge('2026-10-29', '2026-11-10')).toBe('12 d');
    expect(formatAge('2026-11-10', '2026-11-10')).toBe('0 d');
  });

  it('is empty for a date of birth in the future', () => {
    expect(formatAge('2027-01-01', '2026-11-10')).toBe('');
  });
});

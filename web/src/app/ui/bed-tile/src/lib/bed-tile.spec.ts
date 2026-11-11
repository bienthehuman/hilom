import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { BedTile, type BedTilePatient } from './bed-tile';

const PATIENT: BedTilePatient = {
  name: 'DelaCruz, Juan',
  mrn: 'P-000123',
  physician: 'Dr. Jose Santos',
  stay: 'Day 3',
  severeAllergies: [],
  criticalVitals: [],
  link: '/clinical/encounters/e1',
};

describe('BedTile', () => {
  async function render(patient: BedTilePatient | null) {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(BedTile);
    fixture.componentRef.setInput('label', 'Bed 12');
    fixture.componentRef.setInput('patient', patient);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows a free bed quietly, without a link', async () => {
    const el = await render(null);
    expect(el.textContent).toContain('Bed 12');
    expect(el.textContent).toContain('Free');
    expect(el.hasAttribute('data-free')).toBe(true);
    expect(el.querySelector('a')).toBeNull();
  });

  it('links the patient to their stay', async () => {
    const el = await render(PATIENT);
    const link = el.querySelector('a');
    expect(link?.textContent?.trim()).toBe('DelaCruz, Juan');
    expect(link?.getAttribute('href')).toBe('/clinical/encounters/e1');
    expect(el.textContent).toContain('P-000123');
    expect(el.textContent).toContain('Dr. Jose Santos');
    expect(el.querySelector('[aria-label="Flags"]')).toBeNull();
    expect(el.hasAttribute('data-flagged')).toBe(false);
    expect(el.hasAttribute('data-link')).toBe(true);
  });

  it('shows the name as text when the viewer cannot open the stay', async () => {
    const el = await render({ ...PATIENT, link: null });
    expect(el.querySelector('a')).toBeNull();
    expect(el.textContent).toContain('DelaCruz, Juan');
  });

  it('flags severe allergies and critical vitals in words, not only color', async () => {
    const el = await render({
      ...PATIENT,
      severeAllergies: ['Aspirin', 'Penicillin'],
      criticalVitals: ['Heart rate', 'SpO₂'],
    });
    const flags = [...el.querySelectorAll('[aria-label="Flags"] li')].map((li) =>
      li.textContent?.trim(),
    );
    expect(flags).toEqual(['Allergy: Aspirin, Penicillin', 'Critical: Heart rate, SpO₂']);
    expect(el.hasAttribute('data-flagged')).toBe(true);
  });
});

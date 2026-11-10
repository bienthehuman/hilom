import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { type PatientSummaryResponse } from '../api/models/patient-summary-response';
import { PatientSearch } from './patient-search';

const JUAN: PatientSummaryResponse = {
  id: 'p1',
  mrn: 'MRN-000123',
  firstName: 'Juan',
  middleName: 'Santos',
  lastName: 'Dela Cruz',
  birthDate: '1958-03-21',
  sex: 'male',
  contactNumber: '0917 555 0199',
  registeredAt: '2026-11-10T01:00:00Z',
};
const PEDRO: PatientSummaryResponse = { ...JUAN, id: 'p2', mrn: 'MRN-000124', firstName: 'Pedro' };

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function setup() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(PatientSearch);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const input = element.querySelector('input')!;
  const http = TestBed.inject(HttpTestingController);
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

  const type = async (text: string) => {
    input.focus();
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await wait(300);
    fixture.detectChanges();
  };
  const press = async (key: string) => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    await fixture.whenStable();
  };
  /** Answers the pending search; returns its query parameter. */
  const answer = async (items: PatientSummaryResponse[]) => {
    const request = http.expectOne((r) => r.url === '/api/patients');
    request.flush({ items, total: items.length, page: 1, pageSize: 6 });
    await fixture.whenStable();
    return request.request.params.get('q');
  };
  return { element, input, http, navigate, type, press, answer };
}

describe('PatientSearch', () => {
  it('searches by what was typed and lists matches with MRN, age and sex', async () => {
    const { element, input, type, answer } = await setup();

    await type('dela');
    expect(await answer([JUAN, PEDRO])).toBe('dela');

    const options = [...element.querySelectorAll('[role="option"]')].map((o) =>
      o.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(options[0]).toMatch(/^Dela Cruz, Juan Santos\s*MRN-000123 · \d+ y · M$/);
    expect(options.at(-1)).toBe('See all 2 results in Patients');
    expect(input.getAttribute('aria-expanded')).toBe('true');
  });

  it('opens the highlighted patient with the arrow keys and Enter', async () => {
    const { input, navigate, type, press, answer } = await setup();
    await type('dela');
    await answer([JUAN, PEDRO]);

    await press('ArrowDown');
    await press('ArrowDown');
    expect(input.getAttribute('aria-activedescendant')).toBe('patient-search-option-1');
    await press('Enter');

    expect(navigate).toHaveBeenCalledWith(['/patients', 'p2']);
  });

  it('goes to the registry when Enter is pressed without choosing', async () => {
    const { navigate, type, press, answer } = await setup();
    await type('dela');
    await answer([JUAN]);

    await press('Enter');

    expect(navigate).toHaveBeenCalledWith(['/patients'], { queryParams: { q: 'dela' } });
  });

  it('says when nothing matches, and closes on Escape', async () => {
    const { element, input, type, press, answer } = await setup();
    await type('zzz');
    await answer([]);

    expect(element.textContent).toContain('No patients match "zzz".');
    expect(element.querySelector('[role="listbox"]')).toBeNull();
    expect(input.getAttribute('aria-expanded')).toBe('false');

    await press('Escape');
    expect(element.textContent).not.toContain('No patients match');
  });

  it('waits for two characters before searching', async () => {
    const { http, type } = await setup();

    await type('d');

    http.expectNone((r) => r.url === '/api/patients');
  });
});

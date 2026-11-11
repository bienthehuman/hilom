import { TestBed } from '@angular/core/testing';

import { BedPicker, type BedPickerWard } from './bed-picker';

const WARDS: BedPickerWard[] = [
  {
    id: 'w3',
    code: 'W3',
    name: 'Medical',
    beds: [
      { id: 'b1', label: 'Bed 1' },
      { id: 'b2', label: 'Bed 2' },
    ],
  },
  { id: 'w4', code: 'W4', name: 'Surgical', beds: [] },
];

describe('BedPicker', () => {
  async function render(value: string | null = null) {
    const fixture = TestBed.createComponent(BedPicker);
    fixture.componentRef.setInput('wards', WARDS);
    fixture.componentRef.setInput('value', value);
    await fixture.whenStable();
    return fixture;
  }

  it('groups beds by ward and skips wards with none', async () => {
    const element: HTMLElement = (await render()).nativeElement;
    const legends = [...element.querySelectorAll('legend')].map((l) =>
      l.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(legends).toEqual(['W3 · Medical · 2 free beds']);
    expect(element.querySelectorAll('input[type=radio]')).toHaveLength(2);
  });

  it('names each bed with its ward and reports the choice', async () => {
    const fixture = await render('b1');
    const radios = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input'),
    ];
    expect(radios.map((r) => r.getAttribute('aria-label'))).toEqual(['W3, Bed 1', 'W3, Bed 2']);
    expect(radios[0]?.checked).toBe(true);

    radios[1]?.click();

    expect(fixture.componentInstance.value()).toBe('b2');
  });
});

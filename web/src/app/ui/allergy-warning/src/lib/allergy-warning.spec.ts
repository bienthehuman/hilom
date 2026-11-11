import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import { AllergyWarning, type WarningAllergy } from './allergy-warning';

function render(allergies: WarningAllergy[], matches: string[] = []): HTMLElement {
  const fixture = TestBed.createComponent(AllergyWarning);
  fixture.componentRef.setInput('allergies', allergies);
  fixture.componentRef.setInput('matches', matches);
  fixture.componentRef.setInput('medication', 'Amoxicillin');
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('allergy-warning', () => {
  it('says when there are no active allergies, without alarming', () => {
    const el = render([]);
    const box = el.querySelector('[data-testid="allergy-warning"]')!;
    expect(box.getAttribute('data-level')).toBe('none');
    expect(box.textContent).toContain('No active allergies on record');
  });

  it('asks to check every allergy when none matches by name', () => {
    const el = render([
      { allergen: 'Latex', severity: 'mild' },
      { allergen: 'Penicillin', severity: 'severe', reaction: 'Hives' },
    ]);
    const box = el.querySelector('[data-testid="allergy-warning"]')!;
    expect(box.getAttribute('data-level')).toBe('check');
    expect(box.textContent).toContain('Check these 2 allergies before giving');
    // Most severe first.
    const names = [...el.querySelectorAll('[data-allergen]')].map((n) => n.textContent?.trim());
    expect(names).toEqual(['Penicillin', 'Latex']);
    expect(box.textContent).toContain('Reaction: Hives');
  });

  it('puts a name match first and in the title, ignoring case', () => {
    const el = render(
      [
        { allergen: 'Penicillin', severity: 'severe' },
        { allergen: 'amoxicillin', severity: 'moderate' },
      ],
      ['Amoxicillin'],
    );
    const box = el.querySelector('[data-testid="allergy-warning"]')!;
    expect(box.getAttribute('data-level')).toBe('match');
    expect(box.textContent).toContain('Possible allergy: amoxicillin');
    expect(el.querySelector('li')?.textContent).toContain('Matches Amoxicillin');
  });
});

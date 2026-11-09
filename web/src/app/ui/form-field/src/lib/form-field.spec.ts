import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HlmInputImports } from '@hilom/ui/input';

import { FormField } from './form-field';
import { firstErrorMessage } from './validation-messages';

describe('firstErrorMessage', () => {
  it('says what is wrong in plain language', () => {
    expect(firstErrorMessage({ required: true }, 'Email')).toBe('Email is required.');
    expect(
      firstErrorMessage({ minlength: { requiredLength: 8, actualLength: 3 } }, 'Password'),
    ).toBe('Use at least 8 characters.');
    expect(firstErrorMessage({ email: true }, 'Email')).toContain('name@example.com');
  });

  it('uses the message from a custom validator', () => {
    expect(
      firstErrorMessage({ slotTaken: { message: 'That slot was just booked.' } }, 'Slot'),
    ).toBe('That slot was just booked.');
  });

  it('returns nothing when the control is valid', () => {
    expect(firstErrorMessage(null, 'Email')).toBeNull();
  });
});

@Component({
  imports: [FormField, ReactiveFormsModule, HlmInputImports],
  template: `
    <ui-form-field label="Full name" for="name" hint="As on the ID" required>
      <input hlmInput id="name" [formControl]="name" />
    </ui-form-field>
  `,
})
class Host {
  readonly name = new FormControl('', { nonNullable: true, validators: Validators.required });
}

describe('FormField', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el, input: el.querySelector('input') as HTMLInputElement };
  }

  it('links the label to the control', async () => {
    const { el } = await render();

    expect(el.querySelector('label')?.getAttribute('for')).toBe('name');
    expect(el.querySelector('label')?.textContent).toContain('(required)');
  });

  it('hides the error until the field is touched', async () => {
    const { el } = await render();

    expect(el.textContent).not.toContain('Full name is required.');
  });

  it('shows the error after the user leaves the field empty', async () => {
    const { fixture, el, input } = await render();

    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(el.textContent).toContain('Full name is required.');
  });
});

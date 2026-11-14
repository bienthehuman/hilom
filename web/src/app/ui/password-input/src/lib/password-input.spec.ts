import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PasswordInputImports } from './password-input';

@Component({
  imports: [PasswordInputImports],
  template: `
    <ui-password-input what="new password">
      <input uiPassword id="new" />
    </ui-password-input>
    <ui-password-input what="confirmed password">
      <input uiPassword id="confirm" />
    </ui-password-input>
  `,
})
class TwoFields {}

describe('PasswordInput', () => {
  async function render() {
    const fixture = TestBed.createComponent(TwoFields);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    return {
      el,
      input: (id: string) => el.querySelector(`#${id}`) as HTMLInputElement,
      button: (label: string) => el.querySelector(`[aria-label="${label}"]`) as HTMLButtonElement,
      settle: () => fixture.whenStable(),
    };
  }

  it('hides each password until its own button is pressed', async () => {
    const { input, button, settle } = await render();
    expect(input('new').type).toBe('password');
    expect(input('confirm').type).toBe('password');

    button('Show confirmed password').click();
    await settle();

    expect(input('confirm').type).toBe('text');
    expect(input('new').type).toBe('password');
    const hide = button('Hide confirmed password');
    expect(hide.getAttribute('aria-pressed')).toBe('true');
    expect(hide.getAttribute('aria-controls')).toBe('confirm');

    hide.click();
    await settle();
    expect(input('confirm').type).toBe('password');
  });
});

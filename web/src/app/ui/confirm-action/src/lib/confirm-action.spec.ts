import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';

import { ConfirmAction, type ConfirmActionHandler } from './confirm-action';

@Component({
  imports: [ConfirmAction],
  template: `<button
    type="button"
    [uiConfirmAction]="action()"
    confirmTitle="Void this reading?"
    confirmLabel="Void reading"
    requireReason
    destructive
  >
    Void
  </button>`,
})
class Host {
  readonly action = signal<ConfirmActionHandler>(async () => undefined);
}

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  fixture.detectChanges();
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  fixture.detectChanges();
}

/** The newest dialog; earlier tests' dialogs may still be closing in the overlay. */
function dialog(): HTMLElement | null {
  return [...document.querySelectorAll<HTMLElement>('[role="alertdialog"]')].at(-1) ?? null;
}

function button(label: string): HTMLButtonElement {
  return [...dialog()!.querySelectorAll('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement;
}

async function open() {
  const fixture = TestBed.createComponent(Host);
  const handler = vi.fn<ConfirmActionHandler>(async () => undefined);
  fixture.componentInstance.action.set(handler);
  await settle(fixture);
  (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
  await settle(fixture);
  return { fixture, handler };
}

describe('confirm-action', () => {
  it('opens an alert dialog from the button', async () => {
    const { fixture } = await open();
    expect(dialog()?.textContent).toContain('Void this reading?');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('button')!.getAttribute('aria-haspopup'),
    ).toBe('dialog');
  });

  it('needs a typed reason before running the action', async () => {
    const { fixture, handler } = await open();
    const reason = dialog()!.querySelector('textarea')!;
    reason.value = '   ';
    reason.dispatchEvent(new Event('input'));
    button('Void reading').click();
    await settle(fixture);
    expect(handler).not.toHaveBeenCalled();

    reason.value = ' Wrong patient ';
    reason.dispatchEvent(new Event('input'));
    button('Void reading').click();
    await settle(fixture);
    expect(handler).toHaveBeenCalledWith('Wrong patient');
  });

  it('keeps the dialog open and shows the error when the action fails', async () => {
    const { fixture, handler } = await open();
    handler.mockRejectedValueOnce({ error: { detail: 'This entry has already been voided.' } });
    const reason = dialog()!.querySelector('textarea')!;
    reason.value = 'Wrong patient';
    reason.dispatchEvent(new Event('input'));
    button('Void reading').click();
    await settle(fixture);
    expect(dialog()?.textContent).toContain('This entry has already been voided.');
    expect(dialog()).not.toBeNull();
  });
});

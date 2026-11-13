import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { DemoAccounts, type DemoLogin } from './demo-accounts';

const accounts = {
  enabled: true,
  password: 'Hilom-Demo-2026',
  accounts: [
    {
      role: 'Physician',
      email: 'physician@demo.hilom.test',
      displayName: 'Dr. Elena Santos',
      description: 'Publish availability.',
      portal: false,
    },
    {
      role: 'Staff Nurse',
      email: 'nurse@demo.hilom.test',
      displayName: 'Maria Reyes',
      description: 'Record vitals.',
      portal: false,
    },
    {
      role: 'Patient Portal User',
      email: 'portal@demo.hilom.test',
      displayName: 'Liza Mercado',
      description: 'Follow a parent.',
      portal: true,
    },
  ],
};

const tick = () => new Promise((resolve) => setTimeout(resolve));

describe('DemoAccounts', () => {
  async function render(options: { portal?: boolean } = {}) {
    TestBed.configureTestingModule({
      imports: [DemoAccounts],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(DemoAccounts);
    fixture.componentRef.setInput('portal', options.portal ?? false);
    const signIns: DemoLogin[] = [];
    fixture.componentInstance.signIn.subscribe((login) => signIns.push(login));
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => {
      await tick();
      fixture.detectChanges();
      await fixture.whenStable();
    };
    fixture.detectChanges();
    return { el, http, settle, signIns };
  }

  it('shows a loading placeholder until the API answers', async () => {
    const { el, http, settle } = await render();

    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();

    http.expectOne('/api/demo/accounts').flush(accounts);
    await settle();

    expect(el.querySelector('[aria-busy="true"]')).toBeNull();
  });

  it('lists the staff roles on the staff sign-in, without the portal account', async () => {
    const { el, http, settle } = await render();

    http.expectOne('/api/demo/accounts').flush(accounts);
    await settle();

    const buttons = Array.from(el.querySelectorAll('button')).map((b) => b.textContent);
    expect(el.textContent).toContain('Try the demo');
    expect(buttons).toHaveLength(2);
    expect(buttons[0]).toContain('Physician');
    expect(buttons[0]).toContain('Dr. Elena Santos');
    expect(el.textContent).not.toContain('Patient Portal User');
  });

  it('treats a missing portal flag as the staff sign-in, as the router binds it', async () => {
    TestBed.configureTestingModule({
      imports: [DemoAccounts],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(DemoAccounts);
    fixture.componentRef.setInput('portal', undefined);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/demo/accounts').flush(accounts);
    await tick();
    fixture.detectChanges();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    expect(buttons).toHaveLength(2);
    expect(buttons[0]?.textContent).toContain('Physician');
  });

  it('lists only the portal account on the patient portal sign-in', async () => {
    const { el, http, settle } = await render({ portal: true });

    http.expectOne('/api/demo/accounts').flush(accounts);
    await settle();

    const buttons = Array.from(el.querySelectorAll('button'));
    expect(buttons).toHaveLength(1);
    expect(buttons[0]?.textContent).toContain('Patient Portal User');
  });

  it('hands the chosen account and the published password to the page', async () => {
    const { el, http, settle, signIns } = await render();
    http.expectOne('/api/demo/accounts').flush(accounts);
    await settle();

    (el.querySelectorAll('button')[1] as HTMLButtonElement).click();

    expect(signIns).toEqual([{ email: 'nurse@demo.hilom.test', password: 'Hilom-Demo-2026' }]);
  });

  it('renders nothing when demo mode is off', async () => {
    const { el, http, settle } = await render();

    http.expectOne('/api/demo/accounts').flush({ enabled: false, password: null, accounts: [] });
    await settle();

    expect(el.textContent?.trim()).toBe('');
    expect(el.querySelector('button')).toBeNull();
  });

  it('says so, and offers a retry, when the logins cannot be loaded', async () => {
    const { el, http, settle } = await render();

    http.expectOne('/api/demo/accounts').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(el.textContent).toContain("Demo logins couldn't be loaded.");
    (el.querySelector('button') as HTMLButtonElement).click();
    await tick(); // the reload request is now pending, which would stop the page settling
    http.expectOne('/api/demo/accounts').flush(accounts);
    await settle();

    expect(el.textContent).toContain('Try the demo');
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { LoginPage } from './login-page';

const tick = () => new Promise((resolve) => setTimeout(resolve));

const demoOff = { enabled: false, password: null, accounts: [] };

describe('LoginPage', () => {
  /** `demo` is what the API says about demo mode: off, unless a test is about the demo logins. */
  async function render(
    inputs: { returnUrl?: string; reason?: string } = {},
    demo: object = demoOff,
  ) {
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(LoginPage);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/demo/accounts').flush(demo);
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    const type = (selector: string, value: string) => {
      const input = el.querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      await fixture.whenStable();
      await tick();
    };
    const settle = async () => {
      await tick();
      fixture.detectChanges();
      await fixture.whenStable();
    };

    return {
      el,
      type,
      submit,
      settle,
      http,
      router: TestBed.inject(Router),
    };
  }

  it('shows field errors without calling the API when the form is empty', async () => {
    const { el, submit, settle, http } = await render();

    await submit();
    await settle();

    expect(el.textContent).toContain('Email is required.');
    expect(el.textContent).toContain('Password is required.');
    http.expectNone('/api/access/login');
  });

  it('explains a failed sign-in and clears the password', async () => {
    const { el, type, submit, settle, http } = await render();
    type('#login-email', 'admin@hilom.test');
    type('#login-password', 'wrong');

    await submit();
    http
      .expectOne('/api/access/login')
      .flush(
        { code: 'invalid_credentials', detail: 'Email or password is incorrect.' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await settle();

    expect(el.textContent).toContain('Sign-in failed');
    expect(el.textContent).toContain('Email or password is incorrect.');
    expect((el.querySelector('#login-password') as HTMLInputElement).value).toBe('');
  });

  it('explains a locked account', async () => {
    const { el, type, submit, settle, http } = await render();
    type('#login-email', 'admin@hilom.test');
    type('#login-password', 'wrong');

    await submit();
    http.expectOne('/api/access/login').flush(
      {
        code: 'account_locked',
        detail: 'Too many failed attempts. Try again in 15 minutes.',
      },
      { status: 401, statusText: 'Unauthorized' },
    );
    await settle();

    expect(el.textContent).toContain('Account temporarily locked');
    expect(el.textContent).toContain('Try again in 15 minutes.');
  });

  it('goes back to the page the user came from after signing in', async () => {
    const { type, submit, http, router } = await render({ returnUrl: '/design' });
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    type('#login-email', 'admin@hilom.test');
    type('#login-password', 'secret');

    await submit();
    http.expectOne('/api/access/login').flush({
      id: '1',
      email: 'admin@hilom.test',
      displayName: 'Admin',
      accountType: 'staff',
      permissions: {},
    });
    await tick();

    expect(navigate).toHaveBeenCalledWith('/design');
  });

  it('signs in as a demo account with one click, through the normal sign-in', async () => {
    const { el, settle, http, router } = await render(
      {},
      {
        enabled: true,
        password: 'Hilom-Demo-2026',
        accounts: [
          {
            role: 'Staff Nurse',
            email: 'nurse@demo.hilom.test',
            displayName: 'Maria Reyes',
            description: 'Record vitals.',
            portal: false,
          },
        ],
      },
    );
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    await settle();

    (el.querySelector('section button') as HTMLButtonElement).click();
    await tick();

    const request = http.expectOne('/api/access/login');
    expect(request.request.body).toEqual({
      email: 'nurse@demo.hilom.test',
      password: 'Hilom-Demo-2026',
    });
    request.flush({
      id: '2',
      email: 'nurse@demo.hilom.test',
      displayName: 'Maria Reyes',
      accountType: 'staff',
      permissions: {},
    });
    await tick();

    expect(navigate).toHaveBeenCalled();
  });

  it('offers no demo logins when demo mode is off', async () => {
    const { el, settle } = await render();
    await settle();

    expect(el.textContent).not.toContain('Try the demo');
  });

  it('tells the user when their session ended', async () => {
    const { el } = await render({ reason: 'expired' });

    expect(el.textContent).toContain('Your session ended');
  });

  it('can reveal the password', async () => {
    const { el, settle } = await render();
    const toggle = el.querySelector('[aria-label="Show password"]') as HTMLButtonElement;

    toggle.click();
    await settle();

    expect((el.querySelector('#login-password') as HTMLInputElement).type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
  });
});

import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { type CurrentUserResponse } from '../../api/models/current-user-response';
import { toProblem } from '../http/problem';
import {
  authGuard,
  guestGuard,
  landingUrl,
  portalAccountGuard,
  portalGuard,
  safeReturnUrl,
  signedInGuard,
} from './auth.guards';
import { AuthService } from './auth.service';
import { sessionInterceptor } from './session.interceptor';

const ADMIN: CurrentUserResponse = {
  id: '0193',
  email: 'admin@hilom.test',
  displayName: 'System Administrator',
  accountType: 'staff',
  mustChangePassword: false,
  mustVerifyEmail: false,
  permissions: {},
};

const DAUGHTER: CurrentUserResponse = {
  id: '0194',
  email: 'maria@family.test',
  displayName: 'Maria Dela Cruz',
  accountType: 'portal',
  mustChangePassword: false,
  mustVerifyEmail: false,
  permissions: { 'patients.view': ['linked'] },
};

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(withInterceptors([sessionInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return {
    auth: TestBed.inject(AuthService),
    http: TestBed.inject(HttpTestingController),
    router: TestBed.inject(Router),
  };
}

/** Lets the API's promise-based calls subscribe before we answer them. */
const tick = () => new Promise((resolve) => setTimeout(resolve));

describe('AuthService', () => {
  it('starts signed in when a session already exists', async () => {
    const { auth, http } = setup();

    const done = auth.initialize();
    await tick();
    http
      .expectOne('/api/access/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    await tick();
    http.expectOne('/api/access/me').flush(ADMIN);
    await done;

    expect(auth.status()).toBe('authenticated');
    expect(auth.user()?.displayName).toBe('System Administrator');
  });

  it('starts signed out when there is no session', async () => {
    const { auth, http } = setup();

    const done = auth.initialize();
    await tick();
    http
      .expectOne('/api/access/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    await tick();
    http.expectOne('/api/access/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await done;

    expect(auth.status()).toBe('anonymous');
  });

  it('signs in and out', async () => {
    const { auth, http } = setup();

    const signingIn = auth.login('admin@hilom.test', 'secret');
    await tick();
    const loginRequest = http.expectOne('/api/access/login');
    expect(loginRequest.request.body).toEqual({ email: 'admin@hilom.test', password: 'secret' });
    loginRequest.flush(ADMIN);
    await signingIn;
    expect(auth.isAuthenticated()).toBe(true);

    const signingOut = auth.logout();
    await tick();
    http.expectOne('/api/access/logout').flush(null, { status: 204, statusText: 'No Content' });
    await signingOut;
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('passes sign-in errors to the page', async () => {
    const { auth, http } = setup();

    const signingIn = auth.login('admin@hilom.test', 'wrong');
    await tick();
    http
      .expectOne('/api/access/login')
      .flush(
        { code: 'invalid_credentials', detail: 'Email or password is incorrect.' },
        { status: 401, statusText: 'Unauthorized' },
      );

    const problem = await signingIn.then(
      () => null,
      (error: unknown) => toProblem(error),
    );
    expect(problem?.code).toBe('invalid_credentials');
    expect(auth.isAuthenticated()).toBe(false);
  });
});

describe('guards', () => {
  const run = (guard: typeof authGuard, url = '/patients') =>
    TestBed.runInInjectionContext(() => guard({} as never, { url } as never));

  it('sends signed-out users to sign-in with a way back', () => {
    const { router } = setup();

    const result = run(authGuard);

    expect(router.serializeUrl(result as never)).toBe('/login?returnUrl=%2Fpatients');
  });

  it('keeps signed-in users away from the sign-in page', async () => {
    const { auth, http, router } = setup();
    const signingIn = auth.login('a@b.c', 'x');
    await tick();
    http.expectOne('/api/access/login').flush(ADMIN);
    await signingIn;

    expect(run(authGuard)).toBe(true);
    expect(router.serializeUrl(run(guestGuard) as never)).toBe('/');
  });

  it('keeps portal accounts in the portal and staff in the staff app', async () => {
    const { auth, http, router } = setup();
    const signingIn = auth.login('maria@family.test', 'x');
    await tick();
    http.expectOne('/api/access/login').flush(DAUGHTER);
    await signingIn;

    expect(auth.isPortal()).toBe(true);
    expect(router.serializeUrl(run(authGuard) as never)).toBe('/portal');
    expect(run(portalGuard, '/portal/patients')).toBe(true);
    expect(router.serializeUrl(run(guestGuard) as never)).toBe('/portal');
  });

  it('sends signed-out portal visitors to the portal sign-in', () => {
    const { router } = setup();

    const result = run(portalGuard, '/portal/patients');

    expect(router.serializeUrl(result as never)).toBe(
      '/portal/login?returnUrl=%2Fportal%2Fpatients',
    );
  });

  it('holds users with a temporary password on the change-password page until they replace it', async () => {
    const { auth, http, router } = setup();
    const signingIn = auth.login('new.nurse@hilom.test', 'x');
    await tick();
    http.expectOne('/api/access/login').flush({ ...ADMIN, mustChangePassword: true });
    await signingIn;

    expect(router.serializeUrl(run(authGuard, '/patients') as never)).toBe(
      '/account/password?returnUrl=%2Fpatients',
    );
    expect(run(signedInGuard, '/account/password')).toBe(true);

    const changing = auth.changePassword('Temporary-Pass-2026', 'My-Own-Choice-2026');
    await tick();
    const request = http.expectOne('/api/access/me/password');
    expect(request.request.body).toEqual({
      currentPassword: 'Temporary-Pass-2026',
      newPassword: 'My-Own-Choice-2026',
    });
    request.flush(ADMIN);
    await changing;

    expect(auth.mustChangePassword()).toBe(false);
    expect(run(authGuard)).toBe(true);
  });

  it('holds portal accounts with a temporary password too', async () => {
    const { auth, http, router } = setup();
    const signingIn = auth.login('maria@family.test', 'x');
    await tick();
    http.expectOne('/api/access/login').flush({ ...DAUGHTER, mustChangePassword: true });
    await signingIn;

    expect(router.serializeUrl(run(portalGuard, '/portal/appointments') as never)).toBe(
      '/account/password?returnUrl=%2Fportal%2Fappointments',
    );
  });

  it('keeps new portal accounts on the welcome page until they confirm their email', async () => {
    const { auth, http, router } = setup();
    const signingIn = auth.login('maria@family.test', 'x');
    await tick();
    http.expectOne('/api/access/login').flush({ ...DAUGHTER, mustVerifyEmail: true });
    await signingIn;

    expect(router.serializeUrl(run(portalGuard, '/portal/appointments') as never)).toBe(
      '/portal/welcome',
    );
    expect(run(portalAccountGuard, '/portal/welcome')).toBe(true);

    const verifying = auth.verifyEmail('123456');
    await tick();
    const request = http.expectOne('/api/access/me/email/verify');
    expect(request.request.body).toEqual({ code: '123456' });
    request.flush(DAUGHTER);
    await verifying;

    expect(run(portalGuard, '/portal/appointments')).toBe(true);
  });

  it('keeps staff off the portal welcome page', async () => {
    const { auth, http, router } = setup();
    const signingIn = auth.login('a@b.c', 'x');
    await tick();
    http.expectOne('/api/access/login').flush(ADMIN);
    await signingIn;

    expect(router.serializeUrl(run(portalAccountGuard, '/portal/welcome') as never)).toBe('/');
  });

  it('sends signed-out visitors of the change-password page to sign-in', () => {
    const { router } = setup();

    expect(router.serializeUrl(run(signedInGuard, '/account/password') as never)).toBe(
      '/login?returnUrl=%2Faccount%2Fpassword',
    );
  });

  it('only returns to paths on this site', () => {
    expect(safeReturnUrl('/patients/42')).toBe('/patients/42');
    expect(safeReturnUrl('https://evil.example')).toBe('/');
    expect(safeReturnUrl('//evil.example')).toBe('/');
    expect(safeReturnUrl('/login')).toBe('/');
    expect(safeReturnUrl(undefined)).toBe('/');
    expect(safeReturnUrl('/portal/register')).toBe('/');
    expect(safeReturnUrl('/account/password')).toBe('/');
    expect(safeReturnUrl('/portal/welcome')).toBe('/');
  });

  it('lands each kind of account in its own part of the app', () => {
    expect(landingUrl(true, '/portal/patients')).toBe('/portal/patients');
    expect(landingUrl(true, '/patients/42')).toBe('/portal');
    expect(landingUrl(true, undefined)).toBe('/portal');
    expect(landingUrl(false, '/patients/42')).toBe('/patients/42');
    expect(landingUrl(false, '/portal/patients')).toBe('/');
  });
});

describe('sessionInterceptor', () => {
  async function signedIn() {
    const context = setup();
    const signingIn = context.auth.login('a@b.c', 'x');
    await tick();
    context.http.expectOne('/api/access/login').flush(ADMIN);
    await signingIn;
    return context;
  }

  it('sends the user to sign-in when the session ends', async () => {
    const { auth, http, router } = await signedIn();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const request = firstValueFrom(TestBed.inject(HttpClient).get('/api/workforce/employees'));
    http
      .expectOne('/api/workforce/employees')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await request.catch(() => undefined);

    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/', reason: 'expired' },
    });
  });

  it('refreshes a stale antiforgery token and retries once', async () => {
    const { http } = await signedIn();

    const request = firstValueFrom(TestBed.inject(HttpClient).post('/api/things', { a: 1 }));
    http
      .expectOne('/api/things')
      .flush({ code: 'invalid_antiforgery_token' }, { status: 400, statusText: 'Bad Request' });
    await tick();
    http
      .expectOne('/api/access/antiforgery')
      .flush(null, { status: 204, statusText: 'No Content' });
    await tick();
    http.expectOne('/api/things').flush({ ok: true });

    await expect(request).resolves.toEqual({ ok: true });
  });
});

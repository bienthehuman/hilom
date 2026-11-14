import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

export const CHANGE_PASSWORD_URL = '/account/password';
export const PORTAL_WELCOME_URL = '/portal/welcome';

/** Users still holding a temporary password go nowhere else until they choose their own. */
function toPasswordChange(router: Router, returnUrl: string) {
  return router.createUrlTree([CHANGE_PASSWORD_URL], { queryParams: { returnUrl } });
}

/**
 * The staff app: signed-in staff only. Signed-out users go to sign-in and come back here afterwards;
 * portal accounts go to the portal.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  if (auth.isPortal()) {
    return router.createUrlTree(['/portal']);
  }
  return auth.mustChangePassword() ? toPasswordChange(router, state.url) : true;
};

/** The patient portal: signed-in portal accounts only. Staff go to the staff app. */
export const portalGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/portal/login'], { queryParams: { returnUrl: state.url } });
  }
  if (!auth.isPortal()) {
    return router.createUrlTree(['/']);
  }
  if (auth.mustChangePassword()) {
    return toPasswordChange(router, state.url);
  }
  return auth.mustVerifyEmail() ? router.createUrlTree([PORTAL_WELCOME_URL]) : true;
};

/** The portal's welcome (onboarding) page: signed-in portal accounts, confirmed or not. */
export const portalAccountGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/portal/login'], { queryParams: { returnUrl: state.url } });
  }
  return auth.isPortal() ? true : router.createUrlTree(['/']);
};

/** Any signed-in account, staff or portal (changing your own password). */
export const signedInGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Only users holding the permission. Others see the "no access" page instead of a broken screen. */
export function permissionGuard(permission: string): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    return auth.can(permission) ? true : inject(Router).createUrlTree(['/forbidden']);
  };
}

/** Sign-in and sign-up pages are pointless when already signed in. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated()
    ? inject(Router).createUrlTree([auth.isPortal() ? '/portal' : '/'])
    : true;
};

/** Only allows same-site paths, so a crafted link can't send users to another site after sign-in. */
export function safeReturnUrl(returnUrl: string | null | undefined): string {
  return returnUrl?.startsWith('/') &&
    !returnUrl.startsWith('//') &&
    !returnUrl.startsWith('/login') &&
    !returnUrl.startsWith(CHANGE_PASSWORD_URL) &&
    !returnUrl.startsWith(PORTAL_WELCOME_URL) &&
    !returnUrl.startsWith('/portal/login') &&
    !returnUrl.startsWith('/portal/register')
    ? returnUrl
    : '/';
}

/** Where to go after signing in: back where the user was, if that's in their part of the app. */
export function landingUrl(isPortal: boolean, returnUrl: string | null | undefined): string {
  const safe = safeReturnUrl(returnUrl);
  const inPortal = safe === '/portal' || safe.startsWith('/portal/');
  if (isPortal) {
    return inPortal ? safe : '/portal';
  }
  return inPortal ? '/' : safe;
}

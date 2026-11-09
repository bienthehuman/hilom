import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import {
  type ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
// Imported directly rather than via '@hilom/ui/utils', which would pull tailwind-merge into the startup bundle.
import { provideSpartanHlm } from './ui/utils/src/lib/provide-spartan-hlm';
import { AuthService } from './core/auth/auth.service';
import { sessionInterceptor } from './core/auth/session.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      withFetch(),
      withInterceptors([sessionInterceptor]),
      // The API issues XSRF-TOKEN; Angular echoes it in X-XSRF-TOKEN on POST/PUT/PATCH/DELETE.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    ),
    provideAppInitializer(() => inject(AuthService).initialize()),
    provideSpartanHlm(),
  ],
};

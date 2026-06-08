import {
  ApplicationConfig,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { bearerInterceptor } from './auth/bearer.interceptor';
import { unauthorizedInterceptor } from './auth/unauthorized.interceptor';
import { restoreSession } from './auth/session-restore.initializer';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([bearerInterceptor, unauthorizedInterceptor])),
    // Blocking: attempt a silent session restore before the router/guards run (S-10).
    provideAppInitializer(restoreSession),
  ],
};

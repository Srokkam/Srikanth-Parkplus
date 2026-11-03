import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { APP_CONFIG, DEFAULT_CONFIG } from './core/api/app-config';
import { HttpShopApi } from './core/api/http-shop-api';
import { problemInterceptor } from './core/api/problem.interceptor';
import { ShopApi } from './core/api/shop-api';
import { authInterceptor } from './core/auth/dev-auth';
import { routes } from './app.routes';

/** Every screen talks to the ASP.NET Core API through ShopApi; `ng serve` proxies /api to it. */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor, problemInterceptor])),
    { provide: APP_CONFIG, useValue: DEFAULT_CONFIG },
    { provide: ShopApi, useClass: HttpShopApi },
  ],
};

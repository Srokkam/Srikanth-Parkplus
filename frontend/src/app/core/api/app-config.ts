import { InjectionToken } from '@angular/core';

export interface AppConfig {
  /** Where the ASP.NET Core API lives. `ng serve` proxies /api to it (see proxy.conf.json). */
  apiBaseUrl: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

export const DEFAULT_CONFIG: AppConfig = {
  apiBaseUrl: '/api',
};

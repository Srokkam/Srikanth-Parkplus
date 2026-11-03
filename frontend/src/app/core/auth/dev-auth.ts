import { HttpBackend, HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, from, switchMap } from 'rxjs';
import { APP_CONFIG } from '../api/app-config';

/** Development sign-in identities. There is no identity provider yet, so the API's development-only endpoint issues tokens. */
const DEV_CUSTOMER = 'dev-customer';
const DEV_ADMIN = 'dev-admin';

/** Local stand-in for sign-in: one token for customer calls and one for admin calls. Replaced by a real login later. */
@Injectable({ providedIn: 'root' })
export class DevAuth {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly base = inject(APP_CONFIG).apiBaseUrl;
  private readonly tokens = new Map<boolean, Promise<string>>();

  ensureToken(admin: boolean): Promise<string> {
    let token = this.tokens.get(admin);
    if (!token) {
      token = firstValueFrom(
        this.http.post<{ accessToken: string }>(`${this.base}/dev/token`, { userId: admin ? DEV_ADMIN : DEV_CUSTOMER, admin }),
      ).then((r) => r.accessToken);

      // A failed sign-in is retried on the next call instead of being cached.
      token.catch(() => this.tokens.delete(admin));
      this.tokens.set(admin, token);
    }

    return token;
  }
}

/** Browsing (products, franchises) is anonymous; admin endpoints get the admin token; everything else the customer token. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const prefix = `${inject(APP_CONFIG).apiBaseUrl}/v1/`;
  if (!req.url.startsWith(prefix)) {
    return next(req);
  }

  const path = req.url.slice(prefix.length);
  if (path.startsWith('products') || path.startsWith('franchises')) {
    return next(req);
  }

  const token = inject(DevAuth).ensureToken(path.startsWith('admin/'));
  return from(token).pipe(switchMap((t) => next(req.clone({ setHeaders: { Authorization: `Bearer ${t}` } }))));
};

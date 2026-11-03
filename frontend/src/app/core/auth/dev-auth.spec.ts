import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { APP_CONFIG } from '../api/app-config';
import { authInterceptor } from './dev-auth';

describe('authInterceptor (development sign-in)', () => {
  let client: HttpClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: '/api' } },
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('lets anyone browse products and franchises without a token', () => {
    client.get('/api/v1/products').subscribe();
    client.get('/api/v1/franchises').subscribe();

    expect(http.expectOne('/api/v1/products').request.headers.has('Authorization')).toBe(false);
    expect(http.expectOne('/api/v1/franchises').request.headers.has('Authorization')).toBe(false);
  });

  it('signs customer calls with one customer token, fetched once', async () => {
    client.get('/api/v1/cart').subscribe();
    client.get('/api/v1/orders').subscribe();

    const token = http.expectOne('/api/dev/token');
    expect(token.request.body).toEqual({ userId: 'dev-customer', admin: false });
    token.flush({ accessToken: 'customer-token' });
    await settle();

    expect(http.expectOne('/api/v1/cart').request.headers.get('Authorization')).toBe('Bearer customer-token');
    expect(http.expectOne('/api/v1/orders').request.headers.get('Authorization')).toBe('Bearer customer-token');
  });

  it('signs admin calls with an admin token', async () => {
    client.post('/api/v1/admin/products', {}).subscribe();

    const token = http.expectOne('/api/dev/token');
    expect(token.request.body).toEqual({ userId: 'dev-admin', admin: true });
    token.flush({ accessToken: 'admin-token' });
    await settle();

    expect(http.expectOne('/api/v1/admin/products').request.headers.get('Authorization')).toBe('Bearer admin-token');
  });
});

import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ApiError } from './api-error';
import { APP_CONFIG } from './app-config';
import { HttpShopApi } from './http-shop-api';
import { problemInterceptor } from './problem.interceptor';

describe('HttpShopApi (the contract with the backend)', () => {
  let api: HttpShopApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        HttpShopApi,
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: '/api' } },
      ],
    });
    api = TestBed.inject(HttpShopApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the filters that are set, as the query string the API expects', () => {
    api.searchProducts({ text: 'cap', franchise: 'csk', type: '', pageSize: 8, continuationToken: 'abc.def' }).subscribe();

    const req = http.expectOne((r) => r.url === '/api/v1/products');
    expect(req.request.params.get('q')).toBe('cap');
    expect(req.request.params.get('franchise')).toBe('csk');
    expect(req.request.params.has('type')).toBe(false);
    expect(req.request.params.get('pageSize')).toBe('8');
    expect(req.request.params.get('continuationToken')).toBe('abc.def');
    req.flush({ items: [], continuationToken: null, totalCount: 0 });
  });

  it('reads the franchises the catalogue uses', async () => {
    const franchises = firstValueFrom(api.listFranchises());
    http.expectOne('/api/v1/franchises').flush([{ code: 'csk' }, { code: 'mi' }]);
    expect(await franchises).toEqual([{ code: 'csk' }, { code: 'mi' }]);
  });

  it('creates a product with a POST of the exact body the admin API expects', async () => {
    const product = {
      franchiseId: 'kkr',
      id: 'kkr-signed-photo',
      type: 'signed-photo' as const,
      name: 'KKR Signed Photo',
      description: 'Signed by the squad.',
      pricePaise: 499900,
      limitedEdition: true,
      maxPerCustomer: 1,
      initialStock: 2,
    };

    const created = firstValueFrom(api.createProduct(product));
    const req = http.expectOne('/api/v1/admin/products');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(product);
    req.flush({ ...product, available: 2, stockStatus: 'low-stock' }, { status: 201, statusText: 'Created' });

    expect((await created).stockStatus).toBe('low-stock');
  });

  it('filters order history by a UTC date range', () => {
    api.listOrders({ from: '2026-09-30T18:30:00.000Z', to: '2026-10-31T18:30:00.000Z', pageSize: 10 }).subscribe();

    const req = http.expectOne((r) => r.url === '/api/v1/orders');
    expect(req.request.params.get('from')).toBe('2026-09-30T18:30:00.000Z');
    expect(req.request.params.get('to')).toBe('2026-10-31T18:30:00.000Z');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush({ items: [], continuationToken: null, totalCount: null });
  });

  it('sends the version it read in If-Match when an admin changes a price or stock', () => {
    api.changePrice('csk', 'csk-cap', 54900, 'price review', '"p1"').subscribe();
    const price = http.expectOne('/api/v1/admin/products/csk/csk-cap/price');
    expect(price.request.method).toBe('PUT');
    expect(price.request.headers.get('If-Match')).toBe('"p1"');
    expect(price.request.body).toEqual({ pricePaise: 54900, reason: 'price review' });
    price.flush({ productId: 'csk-cap', pricePaise: 54900 });

    api.setStock('csk', 'csk-cap', 120, 'recount', '"s1"').subscribe();
    const stock = http.expectOne('/api/v1/admin/products/csk/csk-cap/stock');
    expect(stock.request.headers.get('If-Match')).toBe('"s1"');
    expect(stock.request.body).toEqual({ available: 120, reason: 'recount' });
    stock.flush({ productId: 'csk-cap', available: 120, stockStatus: 'in-stock' });
  });

  it('turns a stale edit (412) into an ApiError the page can recognise', async () => {
    const result = firstValueFrom(api.changePrice('csk', 'csk-cap', 54900, 'price review', '"old"'));
    http.expectOne('/api/v1/admin/products/csk/csk-cap/price').flush(
      { status: 412, code: 'precondition-failed', detail: 'Someone changed this since you loaded it. Reload and try again.' },
      { status: 412, statusText: 'Precondition Failed' },
    );

    const error = (await result.catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(412);
    expect(error.code).toBe('precondition-failed');
    expect(error.retryable).toBe(false);
  });

  it('sends the idempotency key on every cart change', () => {
    api.addToCart('key-1', 'csk', 'csk-cap', 2).subscribe();
    const add = http.expectOne('/api/v1/cart/items');
    expect(add.request.method).toBe('POST');
    expect(add.request.headers.get('Idempotency-Key')).toBe('key-1');
    expect(add.request.body).toEqual({ franchiseId: 'csk', productId: 'csk-cap', quantity: 2 });
    add.flush({});

    api.setQuantity('key-2', 'csk-cap', 3).subscribe();
    const set = http.expectOne('/api/v1/cart/items/csk-cap');
    expect(set.request.method).toBe('PUT');
    expect(set.request.headers.get('Idempotency-Key')).toBe('key-2');
    set.flush({});

    api.removeFromCart('key-3', 'csk-cap').subscribe();
    const remove = http.expectOne('/api/v1/cart/items/csk-cap');
    expect(remove.request.method).toBe('DELETE');
    expect(remove.request.headers.get('Idempotency-Key')).toBe('key-3');
    remove.flush({});
  });

  it('puts the order at a client-chosen id and reports whether it was newly created', async () => {
    const first = firstValueFrom(api.placeOrder('3f6c1c0e-6a53-4a39-9c2b-0b4a3d2e9f10', 149700));
    const req = http.expectOne('/api/v1/orders/3f6c1c0e-6a53-4a39-9c2b-0b4a3d2e9f10');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ acceptedTotalPaise: 149700 });
    req.flush({ id: 'x' }, { status: 201, statusText: 'Created' });
    expect((await first).created).toBe(true);

    const repeat = firstValueFrom(api.placeOrder('3f6c1c0e-6a53-4a39-9c2b-0b4a3d2e9f10', 149700));
    http.expectOne('/api/v1/orders/3f6c1c0e-6a53-4a39-9c2b-0b4a3d2e9f10').flush({ id: 'x' }, { status: 200, statusText: 'OK' });
    expect((await repeat).created).toBe(false);
  });

  it('turns problem+json into a typed ApiError, keeping the extension members', async () => {
    const result = firstValueFrom(api.placeOrder('o-1', 100));
    http.expectOne('/api/v1/orders/o-1').flush(
      {
        type: '/problems/out-of-stock',
        status: 409,
        detail: 'Some items cannot be fulfilled.',
        code: 'out-of-stock',
        failedLines: [{ productId: 'csk-cap', requested: 2, available: 0, reason: 'out-of-stock' }],
      },
      { status: 409, statusText: 'Conflict' },
    );

    const error = (await result.catch((e: unknown) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(error.code).toBe('out-of-stock');
    expect(error.failedLines).toHaveLength(1);
    expect(error.retryable).toBe(false);
  });

  it('keeps field errors from a validation problem and Retry-After from a rate limit', async () => {
    const invalid = firstValueFrom(api.addToCart('k', 'csk', 'csk-cap', 99));
    http.expectOne('/api/v1/cart/items').flush(
      { status: 400, code: 'validation-failed', detail: 'One or more fields are invalid', errors: { quantity: ['Quantity must be between 1 and 10.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(((await invalid.catch((e: unknown) => e)) as ApiError).fieldErrors['quantity']).toEqual(['Quantity must be between 1 and 10.']);

    const limited = firstValueFrom(api.getCart());
    http.expectOne('/api/v1/cart').flush({ status: 429, code: 'rate-limited' }, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '7' } });
    const error = (await limited.catch((e: unknown) => e)) as ApiError;
    expect(error.retryable).toBe(true);
    expect(error.retryAfterSeconds).toBe(7);
  });

  it('reports a network failure as retryable', async () => {
    const result = firstValueFrom(api.getCart());
    http.expectOne('/api/v1/cart').error(new ProgressEvent('error'));
    const error = (await result.catch((e: unknown) => e)) as ApiError;
    expect(error.status).toBe(0);
    expect(error.retryable).toBe(true);
  });
});

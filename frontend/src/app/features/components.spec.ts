import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../core/api/api-error';
import { ShopApi } from '../core/api/shop-api';
import { Cart, CartLine, EMPTY_CART, Order, Page, PlacedOrder, Product } from '../core/models';
import { CheckoutStore } from '../core/state/checkout-store';
import { CartPage } from './cart/cart-page';
import { CheckoutPage } from './cart/checkout-page';
import { ProductList } from './products/product-list';

// Each test stubs only the API answers it needs; the real app talks to the backend through HttpShopApi.

function product(id: string, name: string, pricePaise: number): Product {
  return {
    id,
    franchiseId: id.split('-')[0],
    name,
    type: 'cap',
    description: `Official ${name}.`,
    pricePaise,
    limitedEdition: false,
    maxPerCustomer: null,
    available: 50,
    stockStatus: 'in-stock',
  };
}

function line(overrides: Partial<CartLine> = {}): CartLine {
  return {
    productId: 'csk-cap',
    franchiseId: 'csk',
    name: 'CSK Cap',
    quantity: 2,
    acceptedUnitPricePaise: 49900,
    currentUnitPricePaise: 49900,
    lineTotalPaise: 99800,
    priceChanged: false,
    stock: 'ok',
    available: 50,
    ...overrides,
  };
}

function cartOf(cartLine: CartLine, overrides: Partial<Cart> = {}): Cart {
  return {
    ...EMPTY_CART,
    lines: [cartLine],
    subtotalPaise: cartLine.lineTotalPaise,
    acceptedSubtotalPaise: cartLine.acceptedUnitPricePaise * cartLine.quantity,
    totalPaise: cartLine.lineTotalPaise,
    ...overrides,
  };
}

const order: Order = {
  id: 'order-1',
  userId: 'dev-customer',
  createdUtc: '2026-10-28T10:15:00Z',
  lines: [],
  shippingPaise: 0,
  subtotalPaise: 99800,
  totalPaise: 99800,
};

function configure(api: Partial<ShopApi>) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([
        { path: 'products', children: [] },
        { path: 'admin/products/new', children: [] },
        { path: 'orders/:orderId', children: [] },
      ]),
      { provide: ShopApi, useValue: { listFranchises: () => of([]), getCart: () => of(EMPTY_CART), ...api } },
    ],
  });
  sessionStorage.clear();
}

async function render<T>(type: new () => T, inputs: Record<string, unknown> = {}) {
  const fixture = TestBed.createComponent(type);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  await settle(fixture);
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let i = 0; i < 3; i++) {
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fixture.whenStable();
    fixture.detectChanges();
  }
}

const button = (el: HTMLElement, text: string) =>
  Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text)) as HTMLButtonElement | undefined;

describe('ProductList (the home page)', () => {
  const firstPage: Page<Product> = {
    items: [product('csk-cap', 'CSK Cap', 49900), product('csk-jersey', 'CSK Jersey', 249900)],
    continuationToken: 'page-2',
    totalCount: 3,
  };
  const lastPage: Page<Product> = { items: [product('mi-flag', 'MI Flag', 29900)], continuationToken: null, totalCount: null };

  it('asks the API for the first page and shows it with the total and a way to load more', async () => {
    const searchProducts = vi.fn(() => of(firstPage));
    configure({ searchProducts });
    const { el } = await render(ProductList);

    expect(searchProducts).toHaveBeenCalledWith({ text: '', type: '', franchise: '', pageSize: 8, continuationToken: null });
    expect(el.querySelectorAll('.card')).toHaveLength(2);
    expect(el.textContent).toContain('3 product(s) found');
    expect(el.textContent).toContain('₹2,499.00');
    expect(button(el, 'Load more')).toBeDefined();
  });

  it('loads the next page with the continuation token and appends it', async () => {
    const searchProducts = vi.fn().mockReturnValueOnce(of(firstPage)).mockReturnValueOnce(of(lastPage));
    configure({ searchProducts });
    const { fixture, el } = await render(ProductList);

    button(el, 'Load more')!.click();
    await settle(fixture);

    expect(searchProducts).toHaveBeenLastCalledWith(expect.objectContaining({ continuationToken: 'page-2' }));
    expect(Array.from(el.querySelectorAll('.card h2')).map((h) => h.textContent?.trim())).toEqual(['CSK Cap', 'CSK Jersey', 'MI Flag']);
    expect(button(el, 'Load more')).toBeUndefined();
  });

  it('sends the search text and filters from the URL to the API and says when nothing matches', async () => {
    const searchProducts = vi.fn(() => of<Page<Product>>({ items: [], continuationToken: null, totalCount: 0 }));
    configure({ searchProducts });
    const { el } = await render(ProductList, { q: 'cap', franchise: 'csk', type: 'signed-photo' });

    expect(searchProducts).toHaveBeenCalledWith(expect.objectContaining({ text: 'cap', franchise: 'csk', type: 'signed-photo' }));
    expect((el.querySelector('#q') as HTMLInputElement).value).toBe('cap');
    expect(el.textContent).toContain('No products match these filters');
  });

  it('treats a missing query parameter as an empty filter, not the text "undefined"', async () => {
    const searchProducts = vi.fn(() => of(firstPage));
    configure({ searchProducts });
    // This is what the router does for /products with no query string.
    const { el } = await render(ProductList, { q: undefined, franchise: undefined, type: undefined });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    expect((el.querySelector('#q') as HTMLInputElement).value).toBe('');
    expect(searchProducts).toHaveBeenCalledWith(expect.objectContaining({ text: '', franchise: '', type: '' }));

    const franchise = el.querySelector('#franchise') as HTMLSelectElement;
    franchise.value = '';
    franchise.dispatchEvent(new Event('change'));
    expect(navigate).toHaveBeenCalledWith([], { queryParams: { q: null, franchise: null, type: null, pageSize: null } });
  });

  it('puts a new search in the URL, so the results can be shared and the back button works', async () => {
    configure({ searchProducts: () => of(firstPage) });
    const { el } = await render(ProductList);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (el.querySelector('#q') as HTMLInputElement).value = '  jersey ';
    el.querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(navigate).toHaveBeenCalledWith([], { queryParams: { q: 'jersey', franchise: null, type: null, pageSize: null } });
  });

  it('asks the API for the page size chosen in the URL, e.g. 2 per page, and keeps other values out', async () => {
    const searchProducts = vi.fn(() => of(firstPage));
    configure({ searchProducts });
    await render(ProductList, { pageSize: '2' });
    expect(searchProducts).toHaveBeenLastCalledWith(expect.objectContaining({ pageSize: 2 }));

    TestBed.resetTestingModule();
    const unchecked = vi.fn(() => of(firstPage));
    configure({ searchProducts: unchecked });
    await render(ProductList, { pageSize: '5000' });
    expect(unchecked).toHaveBeenLastCalledWith(expect.objectContaining({ pageSize: 8 }));
  });

  it('invites adding the first product when the catalogue is empty', async () => {
    configure({ searchProducts: () => of<Page<Product>>({ items: [], continuationToken: null, totalCount: 0 }) });
    const { el } = await render(ProductList);

    expect(el.textContent).toContain('There are no products yet');
    expect(el.querySelector('a[href="/admin/products/new"]')).not.toBeNull();
  });

  it('offers the franchises the API returns', async () => {
    configure({ searchProducts: () => of(firstPage), listFranchises: () => of([{ code: 'csk' }, { code: 'mi' }]) });
    const { el } = await render(ProductList);

    expect(Array.from(el.querySelectorAll('#franchise option')).map((o) => o.textContent?.trim())).toEqual(['All franchises', 'CSK', 'MI']);
  });

  it('shows a failure with a retry that works once the service is back', async () => {
    const searchProducts = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new ApiError(503, 'dependency-unavailable', 'The service is busy.')))
      .mockReturnValue(of(firstPage));
    configure({ searchProducts });
    const { fixture, el } = await render(ProductList);

    expect(el.querySelector('[role=alert]')?.textContent).toContain('The service is busy.');

    (el.querySelector('[role=alert] button') as HTMLButtonElement).click();
    await settle(fixture);

    expect(el.querySelectorAll('.card')).toHaveLength(2);
    expect(el.querySelector('[role=alert]')).toBeNull();
  });

  it('labels every form control', async () => {
    configure({ searchProducts: () => of(firstPage) });
    const { el } = await render(ProductList);

    const controls = Array.from(el.querySelectorAll('input, select'));
    expect(controls.length).toBe(4);
    for (const control of controls) {
      expect(el.querySelector(`label[for="${control.id}"]`), `label for #${control.id}`).not.toBeNull();
    }
  });
});

describe('CartPage', () => {
  it('warns when a price changed and clears the warning after the customer accepts', async () => {
    const changed = cartOf(line({ currentUnitPricePaise: 59900, lineTotalPaise: 119800, priceChanged: true }), { requiresAcceptance: true });
    const accepted = cartOf(line({ acceptedUnitPricePaise: 59900, currentUnitPricePaise: 59900, lineTotalPaise: 119800 }));
    const acceptChanges = vi.fn(() => of(accepted));
    configure({ getCart: () => of(changed), acceptChanges });
    const { fixture, el } = await render(CartPage);

    const alert = el.querySelector('[role=alert]');
    expect(alert?.textContent).toContain('prices changed');
    expect(alert?.textContent).toContain('₹1,198.00');
    expect(el.textContent).toContain('was ₹499.00');

    (alert!.querySelector('button') as HTMLButtonElement).click();
    await settle(fixture);

    expect(acceptChanges).toHaveBeenCalledWith(expect.any(String), 119800);
    expect(el.querySelector('[role=alert]')).toBeNull();
  });

  it('points at lines that cannot be fulfilled and labels each quantity field', async () => {
    configure({ getCart: () => of(cartOf(line({ stock: 'insufficient', available: 1 }), { hasBlockingIssues: true })) });
    const { el } = await render(CartPage);

    expect(el.textContent).toContain('Only 1 left');
    expect(el.querySelector('label[for="qty-csk-cap"]')?.textContent).toContain('Quantity for CSK Cap');
    expect(el.querySelector('button.link')?.textContent).toContain('Remove');
  });

  it('shows rejected cart changes where the customer is looking', async () => {
    configure({
      getCart: () => of(cartOf(line())),
      setQuantity: () => throwError(() => new ApiError(409, 'insufficient-stock', "Only 2 of 'CSK Cap' left in stock.")),
    });
    const { fixture, el } = await render(CartPage);

    (el.querySelector('select') as HTMLSelectElement).value = '3';
    el.querySelector('select')!.dispatchEvent(new Event('change'));
    await settle(fixture);

    expect(el.querySelector('[role=alert]')?.textContent).toContain('Only 2 of');
  });
});

describe('CheckoutPage', () => {
  async function checkoutPage(placeOrder: ShopApi['placeOrder']) {
    configure({ getCart: () => of(cartOf(line())), placeOrder });
    return render(CheckoutPage);
  }

  it('names the items that ran out and keeps the cart', async () => {
    const outOfStock = new ApiError(409, 'out-of-stock', 'Some items are no longer available.', {
      failedLines: [{ productId: 'csk-cap', requested: 2, available: 0, reason: 'out-of-stock' }],
    });
    const { fixture, el } = await checkoutPage(() => throwError(() => outOfStock));

    button(el, 'Place order')!.click();
    await settle(fixture);

    expect(el.querySelector('[role=alert]')?.textContent).toContain('CSK Cap is out of stock');
    expect(TestBed.inject(CheckoutStore).placed()).toBeNull();
  });

  it('asks the customer to accept a changed price instead of charging it', async () => {
    const changed = new ApiError(409, 'price-changed', 'Prices changed.', { currentTotalPaise: 119800 });
    const { fixture, el } = await checkoutPage(() => throwError(() => changed));

    button(el, 'Place order')!.click();
    await settle(fixture);

    expect(el.querySelector('[role=alert]')?.textContent).toContain('Prices changed');
    expect(el.querySelector('[role=alert]')?.textContent).toContain('₹1,198.00');
  });

  it('tells the customer a retry is safe after a lost response, and retries with the same order id', async () => {
    const placeOrder = vi
      .fn()
      .mockReturnValueOnce(throwError(() => ApiError.network()))
      .mockReturnValueOnce(of<PlacedOrder>({ order, created: false }));
    const { fixture, el } = await checkoutPage(placeOrder);

    button(el, 'Place order')!.click();
    await settle(fixture);
    expect(el.textContent).toContain('you will not be charged twice');

    button(el, 'Try again')!.click();
    await settle(fixture);

    expect(placeOrder).toHaveBeenCalledTimes(2);
    expect(placeOrder.mock.calls[1][0]).toBe(placeOrder.mock.calls[0][0]);
    expect(TestBed.inject(CheckoutStore).placed()).toEqual(order);
  });

  it('disables the button while placing, so a double click cannot place twice', async () => {
    const placeOrder = vi.fn(() => new Subject<PlacedOrder>());
    const { fixture, el } = await checkoutPage(placeOrder);

    const place = button(el, 'Place order')!;
    place.click();
    place.click();
    fixture.detectChanges();
    await settle(fixture);

    expect(placeOrder).toHaveBeenCalledTimes(1);
  });
});

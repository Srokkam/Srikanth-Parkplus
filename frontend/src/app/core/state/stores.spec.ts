import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { ShopApi } from '../api/shop-api';
import { Cart, EMPTY_CART, Order } from '../models';
import { CartStore } from './cart-store';
import { CheckoutStore } from './checkout-store';

const cartWithOneCap: Cart = {
  ...EMPTY_CART,
  lines: [{ productId: 'csk-cap', franchiseId: 'csk', name: 'CSK Cap', quantity: 1, acceptedUnitPricePaise: 49900, currentUnitPricePaise: 49900, lineTotalPaise: 49900, priceChanged: false, stock: 'ok', available: 5 }],
  subtotalPaise: 49900,
  totalPaise: 49900,
};

const order = { id: 'o', totalPaise: 49900 } as Order;
const busy = () => new ApiError(503, 'dependency-unavailable', 'busy');
const rejected = () => new ApiError(409, 'out-of-stock', 'nope');

function setup(api: Partial<ShopApi>) {
  TestBed.configureTestingModule({ providers: [{ provide: ShopApi, useValue: api }] });
  sessionStorage.clear();
  return { cart: TestBed.inject(CartStore), checkout: TestBed.inject(CheckoutStore) };
}

describe('CartStore: double clicks and retries', () => {
  it('ignores a second click while the first request is still in flight', async () => {
    const reply = new Subject<Cart>();
    const addToCart = vi.fn((): Observable<Cart> => reply);
    const { cart } = setup({ addToCart });

    const first = cart.add('csk', 'csk-cap', 1);
    const second = await cart.add('csk', 'csk-cap', 1);
    reply.next(cartWithOneCap);
    reply.complete();

    expect(second).toBe(false);
    expect(await first).toBe(true);
    expect(addToCart).toHaveBeenCalledTimes(1);
  });

  it('repeats the same idempotency key when retrying after a failure, and uses a new one for the next intent', async () => {
    const addToCart = vi.fn().mockReturnValueOnce(throwError(() => busy())).mockReturnValue(of(cartWithOneCap));
    const { cart } = setup({ addToCart });

    expect(await cart.add('csk', 'csk-cap', 1)).toBe(false);
    expect(cart.actionError()?.retryable).toBe(true);
    expect(await cart.add('csk', 'csk-cap', 1)).toBe(true);
    expect(await cart.add('csk', 'csk-cap', 1)).toBe(true);

    const [firstKey, retryKey, nextKey] = addToCart.mock.calls.map((call) => call[0]);
    expect(retryKey).toBe(firstKey);
    expect(nextKey).not.toBe(firstKey);
  });

  it('gives a rejected request a fresh key next time and shows the reason', async () => {
    const addToCart = vi.fn().mockReturnValueOnce(throwError(() => rejected())).mockReturnValue(of(cartWithOneCap));
    const { cart } = setup({ addToCart });

    await cart.add('csk', 'csk-cap', 1);
    expect(cart.actionError()?.code).toBe('out-of-stock');
    await cart.add('csk', 'csk-cap', 1);

    expect(addToCart.mock.calls[1][0]).not.toBe(addToCart.mock.calls[0][0]);
    expect(cart.actionError()).toBeNull();
  });
});

describe('CheckoutStore: one order id per attempt', () => {
  it('keeps the order id across a timeout so the retry cannot create a second order', async () => {
    const placeOrder = vi.fn().mockReturnValueOnce(throwError(() => ApiError.network())).mockReturnValue(of({ order, created: false }));
    const { checkout } = setup({ placeOrder });

    expect(await checkout.place(49900)).toBeNull();
    expect(checkout.error()?.retryable).toBe(true);
    expect(await checkout.place(49900)).toEqual(order);

    expect(placeOrder.mock.calls[1][0]).toBe(placeOrder.mock.calls[0][0]);
  });

  it('starts a new attempt after a final rejection, and after success', async () => {
    const placeOrder = vi.fn().mockReturnValueOnce(throwError(() => rejected())).mockReturnValue(of({ order, created: true }));
    const { checkout } = setup({ placeOrder });

    await checkout.place(49900);
    await checkout.place(49900);
    await checkout.place(49900);

    const ids = placeOrder.mock.calls.map((c) => c[0]);
    expect(ids[1]).not.toBe(ids[0]);
    expect(ids[2]).not.toBe(ids[1]);
  });

  it('ignores a second click while placing, empties the cart on success', async () => {
    const reply = new Subject<{ order: Order; created: boolean }>();
    const placeOrder = vi.fn(() => reply);
    const { checkout, cart } = setup({ placeOrder });
    cart.cart.set(cartWithOneCap);

    const first = checkout.place(49900);
    expect(await checkout.place(49900)).toBeNull();
    reply.next({ order, created: true });
    reply.complete();
    await first;

    expect(placeOrder).toHaveBeenCalledTimes(1);
    expect(cart.cart().lines).toHaveLength(0);
    expect(checkout.placed()).toEqual(order);
  });
});

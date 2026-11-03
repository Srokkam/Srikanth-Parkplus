import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '../api/api-error';
import { ShopApi } from '../api/shop-api';
import { newId } from '../ids';
import { Cart, EMPTY_CART } from '../models';
import { Announcer } from './announcer';

@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly api = inject(ShopApi);
  private readonly announcer = inject(Announcer);

  /** One idempotency key per user intent. It survives a failed attempt so a retry repeats the same request. */
  private readonly keys = new Map<string, string>();

  readonly cart = signal<Cart>(EMPTY_CART);
  readonly loading = signal(false);
  readonly loadError = signal<ApiError | null>(null);
  readonly pending = signal(false);
  readonly actionError = signal<ApiError | null>(null);

  readonly count = computed(() => this.cart().lines.reduce((sum, line) => sum + line.quantity, 0));

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.cart.set(await firstValueFrom(this.api.getCart()));
    } catch (error) {
      this.loadError.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  add(franchiseId: string, productId: string, quantity: number): Promise<boolean> {
    return this.mutate(`add|${franchiseId}|${productId}|${quantity}`, (key) => this.api.addToCart(key, franchiseId, productId, quantity), 'Added to cart.');
  }

  setQuantity(productId: string, quantity: number): Promise<boolean> {
    return this.mutate(`set|${productId}|${quantity}`, (key) => this.api.setQuantity(key, productId, quantity), 'Quantity updated.');
  }

  remove(productId: string): Promise<boolean> {
    return this.mutate(`remove|${productId}`, (key) => this.api.removeFromCart(key, productId), 'Removed from cart.');
  }

  acceptChanges(expectedTotalPaise: number): Promise<boolean> {
    return this.mutate(`accept|${expectedTotalPaise}`, (key) => this.api.acceptChanges(key, expectedTotalPaise), 'New prices accepted.');
  }

  clear(): void {
    this.cart.set(EMPTY_CART);
  }

  private async mutate(intent: string, call: (key: string) => Observable<Cart>, doneMessage: string): Promise<boolean> {
    // A second click while the first is still travelling must not become a second change.
    if (this.pending()) {
      return false;
    }

    this.pending.set(true);
    this.actionError.set(null);

    const key = this.keys.get(intent) ?? newId();
    this.keys.set(intent, key);

    try {
      this.cart.set(await firstValueFrom(call(key)));
      this.keys.delete(intent);
      this.announcer.announce(doneMessage);
      return true;
    } catch (error) {
      const apiError = toApiError(error);
      if (!apiError.retryable) {
        this.keys.delete(intent);
      }
      this.actionError.set(apiError);
      return false;
    } finally {
      this.pending.set(false);
    }
  }
}

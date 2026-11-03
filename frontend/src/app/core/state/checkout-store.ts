import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '../api/api-error';
import { ShopApi } from '../api/shop-api';
import { newId } from '../ids';
import { Order } from '../models';
import { Announcer } from './announcer';
import { CartStore } from './cart-store';

const STORAGE_KEY = 'ipl.checkout.attempt';

@Injectable({ providedIn: 'root' })
export class CheckoutStore {
  private readonly api = inject(ShopApi);
  private readonly cart = inject(CartStore);
  private readonly announcer = inject(Announcer);

  readonly placing = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly placed = signal<Order | null>(null);

  /**
   * Places the order. The order id is created once per attempt and kept (also across a page reload) until the server
   * has given a final answer, so retrying after a timeout or a lost response can never create a second order.
   */
  async place(acceptedTotalPaise: number): Promise<Order | null> {
    if (this.placing()) {
      return null;
    }

    this.placing.set(true);
    this.error.set(null);
    const orderId = this.currentOrderId();

    try {
      const { order } = await firstValueFrom(this.api.placeOrder(orderId, acceptedTotalPaise));
      this.forgetAttempt();
      this.cart.clear();
      this.placed.set(order);
      this.announcer.announce('Your order has been placed.');
      return order;
    } catch (error) {
      const apiError = toApiError(error);
      if (!apiError.retryable) {
        this.forgetAttempt();
      }
      this.error.set(apiError);
      return null;
    } finally {
      this.placing.set(false);
    }
  }

  reset(): void {
    this.error.set(null);
    this.placed.set(null);
  }

  private currentOrderId(): string {
    const saved = read();
    if (saved) {
      return saved;
    }

    const id = newId();
    write(id);
    return id;
  }

  private forgetAttempt(): void {
    memory = null;
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // nothing stored
    }
  }
}

// Storage can be unavailable (private windows, blocked cookies): fall back to memory for the life of the page.
let memory: string | null = null;

function read(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY) ?? memory;
  } catch {
    return memory;
  }
}

function write(id: string): void {
  memory = id;
  try {
    sessionStorage.setItem(STORAGE_KEY, id);
  } catch {
    // memory copy is enough
  }
}

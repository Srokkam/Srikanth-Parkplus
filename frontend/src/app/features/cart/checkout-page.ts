import { Component, computed, inject, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CartStore } from '../../core/state/cart-store';
import { CheckoutStore } from '../../core/state/checkout-store';
import { ErrorBanner } from '../../shared/error-banner';
import { RupeesPipe } from '../../shared/rupees.pipe';

const REASONS: Record<string, string> = {
  'out-of-stock': 'is out of stock',
  'insufficient-stock': 'does not have enough stock',
  'product-unavailable': 'is no longer available',
  'limit-exceeded': 'is limited to one per customer and you already bought it',
};

@Component({
  selector: 'app-checkout-page',
  imports: [RouterLink, RupeesPipe, ErrorBanner],
  template: `
    <h1>Checkout</h1>

    @if (cart.loading() && cart.cart().lines.length === 0) {
      <p role="status">Loading your cart…</p>
    } @else if (cart.cart().lines.length === 0) {
      <p>There is nothing to check out. <a routerLink="/products">Browse products</a></p>
    } @else {
      @let c = cart.cart();

      <h2>Review your order</h2>
      <ul>
        @for (line of c.lines; track line.productId) {
          <li>{{ line.quantity }} × {{ line.name }} — {{ line.lineTotalPaise | rupees }}</li>
        }
      </ul>
      <p>Shipping: {{ c.shippingPaise === 0 ? 'Free' : (c.shippingPaise | rupees) }}</p>
      <p class="price">Total: {{ c.totalPaise | rupees }} <span class="muted">incl. taxes</span></p>

      @if (store.error(); as e) {
        @switch (e.code) {
          @case ('price-changed') {
            <div class="banner banner-warn" role="alert">
              <p class="banner-title">Prices changed while you were checking out.</p>
              <p>The current total is {{ e.currentTotalPaise ?? c.totalPaise | rupees }}. Nothing has been charged.</p>
              <button type="button" (click)="acceptAndStay(e.currentTotalPaise ?? c.totalPaise)" [disabled]="cart.pending()">Accept new prices</button>
            </div>
          }
          @case ('out-of-stock') {
            <div class="banner banner-error" role="alert">
              <p class="banner-title">{{ e.message }}</p>
              <ul>
                @for (f of e.failedLines; track f.productId) {
                  <li>{{ nameOf(f.productId) }} {{ reason(f.reason) }}.</li>
                }
              </ul>
              <a routerLink="/cart">Fix your cart</a>
            </div>
          }
          @case ('quantity-limit-exceeded') {
            <div class="banner banner-error" role="alert">
              <p class="banner-title">{{ e.message }}</p>
              <ul>
                @for (f of e.failedLines; track f.productId) {
                  <li>{{ nameOf(f.productId) }} {{ reason(f.reason) }}.</li>
                }
              </ul>
              <a routerLink="/cart">Fix your cart</a>
            </div>
          }
          @default {
            <app-error-banner [error]="e" (retry)="place()" />
            @if (e.retryable) {
              <p class="muted">Trying again is safe: you will not be charged twice.</p>
            }
          }
        }
      }

      <app-error-banner [error]="cart.actionError()" />

      <button type="button" (click)="place()" [disabled]="store.placing() || blocked()">
        {{ store.placing() ? 'Placing order…' : 'Place order' }}
      </button>
      @if (blocked()) {
        <p class="muted">Resolve the issues in your <a routerLink="/cart">cart</a> first.</p>
      }
    }
  `,
})
export class CheckoutPage implements OnInit {
  protected readonly store = inject(CheckoutStore);
  protected readonly cart = inject(CartStore);
  private readonly router = inject(Router);

  protected readonly blocked = computed(() => this.cart.cart().hasBlockingIssues || this.cart.cart().requiresAcceptance);

  ngOnInit(): void {
    this.store.reset();
    void this.cart.load();
  }

  protected async place(): Promise<void> {
    const order = await this.store.place(this.cart.cart().totalPaise);
    if (order) {
      await this.router.navigate(['/orders', order.id], { state: { justPlaced: true } });
    }
  }

  protected async acceptAndStay(total: number): Promise<void> {
    if (await this.cart.acceptChanges(total)) {
      this.store.reset();
    }
  }

  protected nameOf(productId: string): string {
    return this.cart.cart().lines.find((l) => l.productId === productId)?.name ?? productId;
  }

  protected reason(reason: string): string {
    return REASONS[reason] ?? 'cannot be ordered';
  }
}

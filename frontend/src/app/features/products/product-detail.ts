import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { Product } from '../../core/models';
import { CartStore } from '../../core/state/cart-store';
import { ErrorBanner } from '../../shared/error-banner';
import { RupeesPipe } from '../../shared/rupees.pipe';
import { StockBadge } from '../../shared/stock-badge';

@Component({
  selector: 'app-product-detail',
  imports: [RouterLink, RupeesPipe, StockBadge, ErrorBanner],
  template: `
    <p><a routerLink="/products">← All products</a></p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading product…</p>
      }
      @case ('error') {
        <app-error-banner [error]="loadError()" (retry)="load()" />
      }
      @default {
        @if (product(); as p) {
          <h1>{{ p.name }}</h1>
          <p>{{ p.description }}</p>
          @if (p.limitedEdition) {
            <p><strong>Limited edition</strong> — one per customer.</p>
          }
          <p class="price">{{ p.pricePaise | rupees }} <span class="muted">incl. taxes</span></p>
          <app-stock-badge [status]="p.stockStatus" [available]="p.available" />

          @if (p.stockStatus !== 'out-of-stock') {
            <form class="inline" (submit)="$event.preventDefault(); add(qty.value)">
              <div class="field">
                <label for="qty">Quantity</label>
                <select #qty id="qty">
                  @for (n of quantities(); track n) {
                    <option [value]="n">{{ n }}</option>
                  }
                </select>
              </div>
              <button type="submit" [disabled]="cart.pending()">{{ cart.pending() ? 'Adding…' : 'Add to cart' }}</button>
            </form>
          }

          <app-error-banner [error]="cart.actionError()" (retry)="add(lastQuantity())" />

          @if (added()) {
            <p class="banner banner-ok" role="status">Added to your cart. <a routerLink="/cart">View cart</a></p>
          }

          <p class="muted"><a [routerLink]="['/admin/products', p.franchiseId, p.id]">Change price or stock (admin)</a></p>
        }
      }
    }
  `,
})
export class ProductDetail {
  private readonly api = inject(ShopApi);
  protected readonly cart = inject(CartStore);
  private readonly router = inject(Router);

  readonly franchiseId = input.required<string>();
  readonly productId = input.required<string>();

  protected readonly product = signal<Product | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadError = signal<ApiError | null>(null);
  protected readonly added = signal(false);
  protected readonly lastQuantity = signal('1');

  protected readonly quantities = computed(() => {
    const p = this.product();
    const max = Math.min(10, p?.maxPerCustomer ?? 10, Math.max(p?.available ?? 1, 1));
    return Array.from({ length: max }, (_, i) => i + 1);
  });

  constructor() {
    effect(() => {
      this.franchiseId();
      this.productId();
      untracked(() => void this.load());
    });
  }

  protected async load(): Promise<void> {
    this.state.set('loading');
    this.added.set(false);
    this.cart.actionError.set(null);
    try {
      this.product.set(await firstValueFrom(this.api.getProduct(this.franchiseId(), this.productId())));
      this.state.set('ready');
    } catch (error) {
      this.loadError.set(toApiError(error));
      this.state.set('error');
    }
  }

  protected async add(quantity: string): Promise<void> {
    this.lastQuantity.set(quantity);
    this.added.set(false);
    const ok = await this.cart.add(this.franchiseId(), this.productId(), Number(quantity));
    this.added.set(ok);

    // Stock may have moved under us: refresh what the page shows, so the next try uses real numbers.
    if (!ok && this.cart.actionError()?.status === 409) {
      const error = this.cart.actionError();
      await this.load();
      this.cart.actionError.set(error);
    }
  }
}

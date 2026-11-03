import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { Order } from '../../core/models';
import { TimeZoneSetting } from '../../core/time';
import { ErrorBanner } from '../../shared/error-banner';
import { LocalTimePipe } from '../../shared/local-time.pipe';
import { RupeesPipe } from '../../shared/rupees.pipe';

@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, LocalTimePipe, RupeesPipe, ErrorBanner],
  template: `
    <p><a routerLink="/orders">← All orders</a></p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading order…</p>
      }
      @case ('error') {
        <app-error-banner [error]="error()" (retry)="load()" />
      }
      @default {
        @if (order(); as o) {
          @if (justPlaced) {
            <div class="banner banner-ok" role="status"><p class="banner-title">Thank you! Your order is confirmed.</p></div>
          }
          <h1>Order placed {{ o.createdUtc | localTime: zones.zone() }}</h1>
          <p class="muted">Order reference {{ o.id }}</p>
          <table>
            <caption class="sr-only">Items in this order</caption>
            <thead>
              <tr><th scope="col">Item</th><th scope="col">Price</th><th scope="col">Quantity</th><th scope="col">Total</th></tr>
            </thead>
            <tbody>
              @for (line of o.lines; track line.productId) {
                <tr>
                  <th scope="row">{{ line.name }}</th>
                  <td>{{ line.unitPricePaise | rupees }}</td>
                  <td>{{ line.quantity }}</td>
                  <td>{{ line.lineTotalPaise | rupees }}</td>
                </tr>
              }
            </tbody>
          </table>
          <dl class="totals">
            <dt>Subtotal</dt><dd>{{ o.subtotalPaise | rupees }}</dd>
            <dt>Shipping</dt><dd>{{ o.shippingPaise === 0 ? 'Free' : (o.shippingPaise | rupees) }}</dd>
            <dt>Total (incl. taxes)</dt><dd class="price">{{ o.totalPaise | rupees }}</dd>
          </dl>
        }
      }
    }
  `,
})
export class OrderDetail {
  private readonly api = inject(ShopApi);
  protected readonly zones = inject(TimeZoneSetting);

  readonly orderId = input.required<string>();

  protected readonly order = signal<Order | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly error = signal<ApiError | null>(null);
  protected readonly justPlaced = inject(Router).getCurrentNavigation()?.extras.state?.['justPlaced'] === true;

  constructor() {
    effect(() => {
      this.orderId();
      untracked(() => void this.load());
    });
  }

  protected async load(): Promise<void> {
    this.state.set('loading');
    try {
      this.order.set(await firstValueFrom(this.api.getOrder(this.orderId())));
      this.state.set('ready');
    } catch (error) {
      this.error.set(toApiError(error));
      this.state.set('error');
    }
  }
}

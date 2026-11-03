import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CartStore } from '../../core/state/cart-store';
import { ErrorBanner } from '../../shared/error-banner';
import { RupeesPipe } from '../../shared/rupees.pipe';

@Component({
  selector: 'app-cart-page',
  imports: [RouterLink, RupeesPipe, ErrorBanner],
  template: `
    <h1>Your cart</h1>

    @if (store.loading() && store.cart().lines.length === 0) {
      <p role="status">Loading your cart…</p>
    } @else if (store.loadError()) {
      <app-error-banner [error]="store.loadError()" (retry)="store.load()" />
    } @else if (store.cart().lines.length === 0) {
      <p>Your cart is empty. <a routerLink="/products">Browse products</a></p>
    } @else {
      @let cart = store.cart();

      @if (cart.requiresAcceptance) {
        <div class="banner banner-warn" role="alert">
          <p class="banner-title">Some prices changed since you added these items.</p>
          <p>The new total is {{ cart.totalPaise | rupees }} (it was {{ cart.acceptedSubtotalPaise | rupees }}).</p>
          <button type="button" (click)="store.acceptChanges(cart.totalPaise)" [disabled]="store.pending()">Accept new prices</button>
        </div>
      }

      <app-error-banner [error]="store.actionError()" />

      <table>
        <caption class="sr-only">Items in your cart</caption>
        <thead>
          <tr>
            <th scope="col">Item</th>
            <th scope="col">Price</th>
            <th scope="col">Quantity</th>
            <th scope="col">Total</th>
            <th scope="col"><span class="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          @for (line of cart.lines; track line.productId) {
            <tr>
              <th scope="row">
                <a [routerLink]="['/products', line.franchiseId, line.productId]">{{ line.name }}</a>
                @if (line.stock === 'unavailable') {
                  <p class="error-text">Out of stock — remove it to continue.</p>
                } @else if (line.stock === 'insufficient') {
                  <p class="error-text">Only {{ line.available }} left — lower the quantity.</p>
                }
              </th>
              <td>
                {{ line.currentUnitPricePaise | rupees }}
                @if (line.priceChanged) {
                  <br /><span class="muted">was {{ line.acceptedUnitPricePaise | rupees }}</span>
                }
              </td>
              <td>
                <label [for]="'qty-' + line.productId" class="sr-only">Quantity for {{ line.name }}</label>
                <select [id]="'qty-' + line.productId" [disabled]="store.pending()" (change)="store.setQuantity(line.productId, +$any($event.target).value)">
                  @for (n of options; track n) {
                    <option [value]="n" [selected]="n === line.quantity">{{ n }}</option>
                  }
                </select>
              </td>
              <td>{{ line.lineTotalPaise | rupees }}</td>
              <td>
                <button type="button" class="link" (click)="store.remove(line.productId)" [disabled]="store.pending()">
                  Remove<span class="sr-only"> {{ line.name }}</span>
                </button>
              </td>
            </tr>
          }
        </tbody>
      </table>

      <dl class="totals">
        <dt>Subtotal</dt>
        <dd>{{ cart.subtotalPaise | rupees }}</dd>
        <dt>Shipping</dt>
        <dd>{{ cart.shippingPaise === 0 ? 'Free' : (cart.shippingPaise | rupees) }}</dd>
        <dt>Total (incl. taxes)</dt>
        <dd class="price">{{ cart.totalPaise | rupees }}</dd>
      </dl>

      <a class="button" routerLink="/checkout">Go to checkout</a>
    }
  `,
})
export class CartPage implements OnInit {
  protected readonly store = inject(CartStore);
  protected readonly options = Array.from({ length: 10 }, (_, i) => i + 1);

  ngOnInit(): void {
    void this.store.load();
  }
}

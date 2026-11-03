import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { Order, OrderQuery } from '../../core/models';
import { TimeZoneSetting, nextDay, startOfDayUtc } from '../../core/time';
import { ErrorBanner } from '../../shared/error-banner';
import { LocalTimePipe } from '../../shared/local-time.pipe';
import { RupeesPipe } from '../../shared/rupees.pipe';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-orders-page',
  imports: [RouterLink, RupeesPipe, LocalTimePipe, ErrorBanner],
  template: `
    <h1>Your orders</h1>

    <form class="filters" (submit)="$event.preventDefault(); apply(fromInput.value, toInput.value)" aria-describedby="range-hint">
      <div class="field">
        <label for="from">From</label>
        <input #fromInput id="from" type="date" [value]="from()" [attr.aria-invalid]="!!rangeError()" />
      </div>
      <div class="field">
        <label for="to">To (inclusive)</label>
        <input #toInput id="to" type="date" [value]="to()" [attr.aria-invalid]="!!rangeError()" />
      </div>
      <div class="field">
        <label for="zone">Show times in</label>
        <select id="zone" (change)="changeZone($any($event.target).value)">
          @for (z of zones.options; track z.value) {
            <option [value]="z.value" [selected]="z.value === zones.zone()">{{ z.label }}</option>
          }
        </select>
      </div>
      <button type="submit">Apply</button>
      @if (from() || to()) {
        <button type="button" class="link" (click)="apply('', '')">Clear dates</button>
      }
    </form>
    <p id="range-hint" class="muted">Dates are whole days in {{ zoneLabel() }}.</p>
    @if (rangeError()) {
      <p class="error-text" role="alert">{{ rangeError() }}</p>
    }

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading your orders…</p>
      }
      @case ('error') {
        <app-error-banner [error]="error()" (retry)="load()" />
      }
      @default {
        @if (orders().length === 0) {
          @if (from() || to()) {
            <p>No orders between these dates.</p>
          } @else {
            <p>You have not placed any orders yet. <a routerLink="/products">Browse products</a></p>
          }
        } @else {
          <table>
            <caption class="sr-only">Orders, newest first</caption>
            <thead>
              <tr>
                <th scope="col">Placed</th>
                <th scope="col">Items</th>
                <th scope="col">Total</th>
              </tr>
            </thead>
            <tbody>
              @for (o of orders(); track o.id) {
                <tr>
                  <th scope="row">
                    <a [routerLink]="['/orders', o.id]">{{ o.createdUtc | localTime: zones.zone() }}</a>
                  </th>
                  <td>{{ itemCount(o) }}</td>
                  <td>{{ o.totalPaise | rupees }}</td>
                </tr>
              }
            </tbody>
          </table>

          @if (more() === 'error') {
            <app-error-banner [error]="error()" (retry)="loadMore()" />
          }
          @if (token()) {
            <button type="button" (click)="loadMore()" [disabled]="more() === 'loading'">
              {{ more() === 'loading' ? 'Loading…' : 'Load more' }}
            </button>
          }
        }
      }
    }
  `,
})
export class OrdersPage implements OnInit {
  private readonly api = inject(ShopApi);
  protected readonly zones = inject(TimeZoneSetting);

  /** Calendar days ("2026-11-03") in the chosen zone; empty means no limit. */
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly rangeError = signal('');

  protected readonly orders = signal<Order[]>([]);
  protected readonly token = signal<string | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly more = signal<'idle' | 'loading' | 'error'>('idle');
  protected readonly error = signal<ApiError | null>(null);

  protected readonly zoneLabel = computed(() => this.zones.options.find((o) => o.value === this.zones.zone())?.label ?? this.zones.zone());

  /** A slow answer for old filters must never overwrite the newer results. */
  private generation = 0;

  ngOnInit(): void {
    void this.load();
  }

  protected apply(from: string, to: string): void {
    if (from && to && from > to) {
      this.rangeError.set('"From" must be on or before "To".');
      return;
    }

    this.rangeError.set('');
    this.from.set(from);
    this.to.set(to);
    void this.load();
  }

  protected changeZone(zone: string): void {
    this.zones.set(zone);
    // The same calendar days start at different instants in another zone.
    if (this.from() || this.to()) {
      void this.load();
    }
  }

  protected async load(): Promise<void> {
    const generation = ++this.generation;
    this.state.set('loading');
    this.more.set('idle');

    try {
      const page = await firstValueFrom(this.api.listOrders(this.query(null)));
      if (generation !== this.generation) return;
      this.orders.set(page.items);
      this.token.set(page.continuationToken);
      this.state.set('ready');
    } catch (error) {
      if (generation !== this.generation) return;
      this.error.set(toApiError(error));
      this.state.set('error');
    }
  }

  protected async loadMore(): Promise<void> {
    const token = this.token();
    if (!token || this.more() === 'loading') return;

    const generation = this.generation;
    this.more.set('loading');
    try {
      const page = await firstValueFrom(this.api.listOrders(this.query(token)));
      if (generation !== this.generation) return;
      this.orders.update((current) => [...current, ...page.items]);
      this.token.set(page.continuationToken);
      this.more.set('idle');
    } catch (error) {
      if (generation !== this.generation) return;
      this.error.set(toApiError(error));
      this.more.set('error');
    }
  }

  /** Uses the API's itemCount when present; an older API without it still works. */
  protected itemCount(order: Order): number {
    return order.itemCount ?? order.lines.reduce((sum, l) => sum + l.quantity, 0);
  }

  private query(continuationToken: string | null): OrderQuery {
    const zone = this.zones.zone();
    return {
      pageSize: PAGE_SIZE,
      continuationToken,
      from: this.from() ? startOfDayUtc(this.from(), zone) : undefined,
      // "To" is inclusive for the shopper and exclusive for the API: everything before the next day starts.
      to: this.to() ? startOfDayUtc(nextDay(this.to()), zone) : undefined,
    };
  }
}

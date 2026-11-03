import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { catchError, firstValueFrom, of } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { PRODUCT_TYPES, Product } from '../../core/models';
import { ErrorBanner } from '../../shared/error-banner';
import { RupeesPipe } from '../../shared/rupees.pipe';
import { StockBadge } from '../../shared/stock-badge';

const PAGE_SIZES = [2, 4, 8, 20, 50] as const;
const DEFAULT_PAGE_SIZE = 8;

const orEmpty = (value: string | undefined): string => value ?? '';

/** ?pageSize=2 from the URL; anything that isn't one of the offered sizes falls back to the default. */
const toPageSize = (value: string | number | undefined): number =>
  (PAGE_SIZES as readonly number[]).includes(Number(value)) ? Number(value) : DEFAULT_PAGE_SIZE;

@Component({
  selector: 'app-product-list',
  imports: [RouterLink, RupeesPipe, StockBadge, ErrorBanner],
  template: `
    <h1>Official IPL merchandise</h1>

    <form class="filters" role="search" (submit)="$event.preventDefault(); apply(text.value, franchiseSelect.value, typeSelect.value, sizeSelect.value)">
      <div class="field">
        <label for="q">Search by name</label>
        <input #text id="q" type="search" [value]="q()" maxlength="50" autocomplete="off" />
      </div>
      <div class="field">
        <label for="franchise">Franchise</label>
        <select #franchiseSelect id="franchise" [value]="franchise()" (change)="apply(text.value, franchiseSelect.value, typeSelect.value, sizeSelect.value)">
          <option value="">All franchises</option>
          @for (f of franchises(); track f.code) {
            <option [value]="f.code" [selected]="f.code === franchise()">{{ f.code.toUpperCase() }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label for="type">Type</label>
        <select #typeSelect id="type" [value]="type()" (change)="apply(text.value, franchiseSelect.value, typeSelect.value, sizeSelect.value)">
          <option value="">All types</option>
          @for (t of types; track t) {
            <option [value]="t" [selected]="t === type()">{{ t }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label for="pageSize">Per page</label>
        <select #sizeSelect id="pageSize" (change)="apply(text.value, franchiseSelect.value, typeSelect.value, sizeSelect.value)">
          @for (n of pageSizes; track n) {
            <option [value]="n" [selected]="n === pageSize()">{{ n }}</option>
          }
        </select>
      </div>
      <button type="submit">Search</button>
    </form>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading products…</p>
      }
      @case ('error') {
        <app-error-banner [error]="error()" (retry)="reload()" />
      }
      @default {
        <p class="muted" role="status">
          @if (total() !== null) {
            {{ total() }} product(s) found
          }
        </p>

        @if (items().length === 0) {
          @if (q() || franchise() || type()) {
            <p>No products match these filters. <a routerLink="/products">Clear the filters</a></p>
          } @else {
            <p>There are no products yet. <a routerLink="/admin/products/new">Add the first product</a></p>
          }
        }

        <ul class="cards">
          @for (p of items(); track p.id) {
            <li class="card">
              <h2>
                <a [routerLink]="['/products', p.franchiseId, p.id]">{{ p.name }}</a>
              </h2>
              <p>{{ p.description }}</p>
              @if (p.limitedEdition) {
                <p><strong>Limited edition</strong></p>
              }
              <p class="price">{{ p.pricePaise | rupees }}</p>
              <app-stock-badge [status]="p.stockStatus" [available]="p.available" />
            </li>
          }
        </ul>

        @if (more() === 'error') {
          <app-error-banner [error]="moreError()" (retry)="loadMore()" />
        }
        @if (token()) {
          <button type="button" (click)="loadMore()" [disabled]="more() === 'loading'">
            {{ more() === 'loading' ? 'Loading…' : 'Load more' }}
          </button>
        }
      }
    }
  `,
})
export class ProductList {
  private readonly api = inject(ShopApi);
  private readonly router = inject(Router);

  // Filters live in the URL, so the back button from a product page returns to the same results.
  // The router sets an input to undefined when its query parameter is absent, so each one falls back to ''.
  readonly q = input('', { transform: orEmpty });
  readonly franchise = input('', { transform: orEmpty });
  readonly type = input('', { transform: orEmpty });
  readonly pageSize = input(DEFAULT_PAGE_SIZE, { transform: toPageSize });

  protected readonly types = PRODUCT_TYPES;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly franchises = toSignal(this.api.listFranchises().pipe(catchError(() => of([]))), { initialValue: [] });

  protected readonly items = signal<Product[]>([]);
  protected readonly total = signal<number | null>(null);
  protected readonly token = signal<string | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly error = signal<ApiError | null>(null);
  protected readonly more = signal<'idle' | 'loading' | 'error'>('idle');
  protected readonly moreError = signal<ApiError | null>(null);

  /** A slow answer for old filters must never overwrite the newer results. */
  private generation = 0;

  constructor() {
    effect(() => {
      this.q();
      this.franchise();
      this.type();
      this.pageSize();
      untracked(() => void this.reload());
    });
  }

  protected apply(text: string, franchise: string, type: string, pageSize: string): void {
    const size = toPageSize(pageSize);
    void this.router.navigate([], {
      queryParams: { q: text.trim() || null, franchise: franchise || null, type: type || null, pageSize: size === DEFAULT_PAGE_SIZE ? null : size },
    });
  }

  protected async reload(): Promise<void> {
    const generation = ++this.generation;
    this.state.set('loading');
    this.more.set('idle');

    try {
      const page = await firstValueFrom(this.api.searchProducts(this.query(null)));
      if (generation !== this.generation) return;
      this.items.set(page.items);
      this.total.set(page.totalCount);
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
      const page = await firstValueFrom(this.api.searchProducts(this.query(token)));
      if (generation !== this.generation) return;
      this.items.update((current) => [...current, ...page.items]);
      this.token.set(page.continuationToken);
      this.more.set('idle');
    } catch (error) {
      if (generation !== this.generation) return;
      this.moreError.set(toApiError(error));
      this.more.set('error');
    }
  }

  private query(continuationToken: string | null) {
    return { text: this.q(), type: this.type(), franchise: this.franchise(), pageSize: this.pageSize(), continuationToken };
  }
}

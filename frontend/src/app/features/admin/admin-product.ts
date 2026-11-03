import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { AdminProduct, AuditEntry } from '../../core/models';
import { formatPaise, rupeesToPaise } from '../../core/money';
import { Announcer } from '../../core/state/announcer';
import { TimeZoneSetting } from '../../core/time';
import { ErrorBanner } from '../../shared/error-banner';
import { LocalTimePipe } from '../../shared/local-time.pipe';
import { RupeesPipe } from '../../shared/rupees.pipe';

const HISTORY_SIZE = 20;

/**
 * Price and stock edits for admins. Each edit sends back the version it was based on (If-Match); if someone else
 * changed the product meanwhile the API refuses with 412, nothing is saved, and this page says so and shows the latest
 * values while keeping what the admin typed.
 */
@Component({
  selector: 'app-admin-product',
  imports: [RouterLink, RupeesPipe, LocalTimePipe, ErrorBanner],
  template: `
    <p><a [routerLink]="['/products', franchiseId(), productId()]">← Back to the product</a></p>

    @switch (state()) {
      @case ('loading') {
        <p role="status">Loading product…</p>
      }
      @case ('error') {
        <app-error-banner [error]="loadError()" (retry)="load()" />
      }
      @default {
        @if (product(); as p) {
          <h1>Manage {{ p.name }}</h1>
          <p>Current price <strong>{{ p.pricePaise | rupees }}</strong> · In stock <strong>{{ p.available }}</strong></p>

          @if (saved()) {
            <div class="banner banner-ok" role="status"><p class="banner-title">{{ saved() }}</p></div>
          }
          @if (conflict()) {
            <div class="banner banner-warn" role="alert">
              <p class="banner-title">Someone else changed this product while you were editing.</p>
              <p>Nothing was saved. The latest values are shown above; check them and save again if your change still applies.</p>
            </div>
          }
          <app-error-banner [error]="actionError()" />

          <form class="stack" (submit)="$event.preventDefault(); savePrice(price.value, priceReason.value)" aria-labelledby="price-heading">
            <h2 id="price-heading">Change the price</h2>
            <div class="field">
              <label for="price">New price (₹, incl. taxes)</label>
              <input #price id="price" type="number" min="0.01" step="0.01" inputmode="decimal" [value]="draftPrice()" />
            </div>
            <div class="field">
              <label for="priceReason">Reason</label>
              <input #priceReason id="priceReason" maxlength="200" autocomplete="off" placeholder="e.g. season pricing" />
            </div>
            <div><button type="submit" [disabled]="saving()">Save price</button></div>
          </form>

          <form class="stack" (submit)="$event.preventDefault(); saveStock(stock.value, stockReason.value)" aria-labelledby="stock-heading">
            <h2 id="stock-heading">Set the stock</h2>
            <div class="field">
              <label for="stock">Units available</label>
              <input #stock id="stock" type="number" min="0" step="1" inputmode="numeric" [value]="draftStock()" />
            </div>
            <div class="field">
              <label for="stockReason">Reason</label>
              <input #stockReason id="stockReason" maxlength="200" autocomplete="off" placeholder="e.g. recount" />
            </div>
            <div><button type="submit" [disabled]="saving()">Save stock</button></div>
          </form>

          <h2>History</h2>
          @for (section of history(); track section.title) {
            <table>
              <caption>{{ section.title }}, newest first</caption>
              <thead>
                <tr><th scope="col">When</th><th scope="col">Change</th><th scope="col">Reason</th><th scope="col">By</th></tr>
              </thead>
              <tbody>
                @for (entry of section.entries; track entry.id) {
                  <tr>
                    <td>{{ entry.tsUtc | localTime: zones.zone() }}</td>
                    <td>{{ describe(entry) }}</td>
                    <td>{{ entry.reason }}</td>
                    <td>{{ entry.actor }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="4">No changes yet.</td></tr>
                }
              </tbody>
            </table>
          }
        }
      }
    }
  `,
})
export class AdminProductPage {
  private readonly api = inject(ShopApi);
  private readonly announcer = inject(Announcer);
  protected readonly zones = inject(TimeZoneSetting);

  readonly franchiseId = input.required<string>();
  readonly productId = input.required<string>();

  protected readonly product = signal<AdminProduct | null>(null);
  protected readonly history = signal<{ title: string; entries: AuditEntry[] }[]>([]);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadError = signal<ApiError | null>(null);

  /** What the inputs start from. Set on load and after a save, never after a conflict, so typing isn't lost. */
  protected readonly draftPrice = signal('');
  protected readonly draftStock = signal('');

  protected readonly saving = signal(false);
  protected readonly saved = signal('');
  protected readonly conflict = signal(false);
  protected readonly actionError = signal<ApiError | null>(null);

  constructor() {
    effect(() => {
      this.franchiseId();
      this.productId();
      untracked(() => void this.load());
    });
  }

  protected async load(): Promise<void> {
    this.state.set('loading');
    try {
      await this.refresh(true);
      this.state.set('ready');
    } catch (error) {
      this.loadError.set(toApiError(error));
      this.state.set('error');
    }
  }

  protected savePrice(rupeesText: string, reason: string): Promise<void> {
    const rupees = Number(rupeesText);
    if (!rupeesText || !(rupees > 0)) {
      return this.invalid('pricePaise', 'Enter a price above ₹0.');
    }

    const pricePaise = rupeesToPaise(rupees);
    return this.save(reason, (p, why) => this.api.changePrice(p.franchiseId, p.id, pricePaise, why, p.priceETag), `Price changed to ${formatPaise(pricePaise)}.`);
  }

  protected saveStock(unitsText: string, reason: string): Promise<void> {
    const units = Number(unitsText);
    if (unitsText === '' || !Number.isInteger(units) || units < 0) {
      return this.invalid('available', 'Enter a whole number, 0 or more.');
    }

    return this.save(reason, (p, why) => this.api.setStock(p.franchiseId, p.id, units, why, p.stockETag), `Stock set to ${units}.`);
  }

  protected describe(entry: AuditEntry): string {
    const show = (value: number | null) => (value === null ? '?' : entry.field === 'price' ? formatPaise(value) : String(value));
    const delta = entry.field === 'stock' && entry.delta !== null ? ` (${entry.delta > 0 ? '+' : ''}${entry.delta})` : '';
    return `${show(entry.oldValue)} → ${show(entry.newValue)}${delta}`;
  }

  private async save(
    reason: string,
    call: (product: AdminProduct, reason: string) => ReturnType<ShopApi['setStock']>,
    doneMessage: string,
  ): Promise<void> {
    const product = this.product();
    if (!product || this.saving()) {
      return;
    }

    const why = reason.trim();
    if (!why) {
      return this.invalid('reason', 'Give a reason: it goes in the audit trail.');
    }

    this.saving.set(true);
    this.saved.set('');
    this.conflict.set(false);
    this.actionError.set(null);

    try {
      await firstValueFrom(call(product, why));
      this.saved.set(doneMessage);
      this.announcer.announce(doneMessage);
      await this.refresh(true);
    } catch (error) {
      const apiError = toApiError(error);
      if (apiError.status === 412) {
        this.conflict.set(true);
        await this.refresh(false).catch(() => undefined);
      } else {
        this.actionError.set(apiError);
      }
    } finally {
      this.saving.set(false);
    }
  }

  private async refresh(resetDrafts: boolean): Promise<void> {
    const [product, prices, stock] = await Promise.all([
      firstValueFrom(this.api.getAdminProduct(this.franchiseId(), this.productId())),
      firstValueFrom(this.api.listAudit(this.franchiseId(), this.productId(), 'price', { pageSize: HISTORY_SIZE })),
      firstValueFrom(this.api.listAudit(this.franchiseId(), this.productId(), 'stock', { pageSize: HISTORY_SIZE })),
    ]);

    this.product.set(product);
    this.history.set([
      { title: 'Price changes', entries: prices.items },
      { title: 'Stock changes', entries: stock.items },
    ]);

    if (resetDrafts) {
      this.draftPrice.set((product.pricePaise / 100).toFixed(2));
      this.draftStock.set(String(product.available));
    }
  }

  private async invalid(field: string, message: string): Promise<void> {
    this.saved.set('');
    this.conflict.set(false);
    this.actionError.set(new ApiError(400, 'validation-failed', 'Check the value and try again.', {}, { [field]: [message] }));
  }
}

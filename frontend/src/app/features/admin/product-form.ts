import { Component, ElementRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, firstValueFrom, map, of } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { NewProduct, PRODUCT_TYPES, Product, ProductType } from '../../core/models';
import { rupeesToPaise } from '../../core/money';
import { Announcer } from '../../core/state/announcer';
import { ErrorBanner } from '../../shared/error-banner';

export interface ProductFormValue {
  franchiseId: string;
  name: string;
  id: string;
  type: string;
  description: string;
  priceRupees: number | null;
  initialStock: number | null;
  limitedEdition: boolean;
  maxPerCustomer: number | null;
}

const MAX_ID_LENGTH = 64;
const SLUG = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** "CSK Cap" in franchise csk -> "csk-cap"; "Home Jersey" in mi -> "mi-home-jersey". */
export function suggestProductId(franchiseId: string, name: string): string {
  const slug = name
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  const code = franchiseId.trim();
  const id = !code || slug === code || slug.startsWith(`${code}-`) ? slug : slug ? `${code}-${slug}` : '';
  return id.slice(0, MAX_ID_LENGTH).replace(/-+$/, '');
}

/**
 * The API's rules, checked before sending so most mistakes never leave the browser. The API stays the authority:
 * anything it rejects is shown next to the same field. Keys are the API's field names.
 */
export function validateProduct(v: ProductFormValue): Record<string, string> {
  const errors: Record<string, string> = {};
  const franchiseId = v.franchiseId.trim();
  const id = v.id.trim();
  const name = v.name.trim();

  if (!/^[a-z]{2,10}$/.test(franchiseId)) {
    errors['franchiseId'] = 'Use 2 to 10 lower-case letters, for example csk.';
  }

  if (!SLUG.test(id) || id.length > MAX_ID_LENGTH) {
    errors['id'] = 'Use lower-case words joined by dashes, for example csk-cap.';
  } else if (!errors['franchiseId'] && !id.startsWith(`${franchiseId}-`)) {
    errors['id'] = `Start the id with "${franchiseId}-".`;
  }

  if (!name) {
    errors['name'] = 'Enter a name.';
  } else if (name.length > 80) {
    errors['name'] = 'Keep the name to 80 characters.';
  }

  if (!(PRODUCT_TYPES as readonly string[]).includes(v.type)) {
    errors['type'] = 'Choose a type.';
  }

  if (v.description.trim().length > 500) {
    errors['description'] = 'Keep the description to 500 characters.';
  }

  if (v.priceRupees === null || !(v.priceRupees > 0)) {
    errors['pricePaise'] = 'Enter a price above ₹0.';
  }

  if (v.initialStock === null || !Number.isInteger(v.initialStock) || v.initialStock < 0) {
    errors['initialStock'] = 'Enter a whole number, 0 or more.';
  }

  if (v.limitedEdition && (v.maxPerCustomer === null || !Number.isInteger(v.maxPerCustomer) || v.maxPerCustomer < 1 || v.maxPerCustomer > 10)) {
    errors['maxPerCustomer'] = 'Enter a whole number from 1 to 10.';
  }

  return errors;
}

export function toNewProduct(v: ProductFormValue): NewProduct {
  return {
    franchiseId: v.franchiseId.trim(),
    id: v.id.trim(),
    type: v.type as ProductType,
    name: v.name.trim(),
    description: v.description.trim(),
    pricePaise: rupeesToPaise(v.priceRupees ?? 0),
    limitedEdition: v.limitedEdition,
    maxPerCustomer: v.limitedEdition ? v.maxPerCustomer : null,
    initialStock: v.initialStock ?? 0,
  };
}

/** Form control id for each API field, so an error can point at its input. */
const INPUT_IDS: Record<string, string> = {
  franchiseId: 'franchiseId',
  id: 'productId',
  name: 'name',
  type: 'type',
  description: 'description',
  pricePaise: 'price',
  initialStock: 'initialStock',
  maxPerCustomer: 'maxPerCustomer',
};

@Component({
  selector: 'app-product-form',
  imports: [ReactiveFormsModule, RouterLink, ErrorBanner],
  template: `
    <h1>Add a product</h1>
    <p class="muted">Creates the product and its opening stock through the API (admin only).</p>

    @if (created(); as p) {
      <div class="banner banner-ok" role="status">
        <p class="banner-title">{{ p.name }} was added.</p>
        <a [routerLink]="['/products', p.franchiseId, p.id]">View it in the store</a>
      </div>
    }

    @if (error(); as e) {
      @if (e.code === 'product-exists') {
        <div class="banner banner-error" role="alert">
          <p class="banner-title">{{ e.message }}</p>
          <p>Ids are never reused. Choose another id, or <a [routerLink]="['/products', sent()?.franchiseId, sent()?.id]">open the existing product</a>.</p>
        </div>
      } @else {
        <app-error-banner [error]="bannerError()" (retry)="submit()" />
      }
    }

    <form class="stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div class="field">
        <label for="franchiseId">Franchise code</label>
        <input id="franchiseId" formControlName="franchiseId" list="franchise-codes" maxlength="10" autocomplete="off" [attr.aria-invalid]="!!message('franchiseId')" aria-describedby="franchiseId-hint franchiseId-error" />
        <datalist id="franchise-codes">
          @for (f of franchises(); track f.code) {
            <option [value]="f.code"></option>
          }
        </datalist>
        <small id="franchiseId-hint" class="muted">Lower-case letters, e.g. csk. A new code adds a new franchise to the store.</small>
        <small id="franchiseId-error" class="error-text">{{ message('franchiseId') }}</small>
      </div>

      <div class="field">
        <label for="name">Name</label>
        <input id="name" formControlName="name" maxlength="80" autocomplete="off" [attr.aria-invalid]="!!message('name')" aria-describedby="name-error" />
        <small id="name-error" class="error-text">{{ message('name') }}</small>
      </div>

      <div class="field">
        <label for="productId">Product id</label>
        <input id="productId" formControlName="id" maxlength="64" autocomplete="off" [attr.aria-invalid]="!!message('id')" aria-describedby="productId-hint productId-error" />
        <small id="productId-hint" class="muted">Suggested from the franchise and name. Ids are permanent and never reused.</small>
        <small id="productId-error" class="error-text">{{ message('id') }}</small>
      </div>

      <div class="field">
        <label for="type">Type</label>
        <select id="type" formControlName="type" [attr.aria-invalid]="!!message('type')" aria-describedby="type-error">
          <option value="">Choose a type</option>
          @for (t of types; track t) {
            <option [value]="t">{{ t }}</option>
          }
        </select>
        <small id="type-error" class="error-text">{{ message('type') }}</small>
      </div>

      <div class="field">
        <label for="description">Description</label>
        <textarea id="description" formControlName="description" rows="3" maxlength="500" [attr.aria-invalid]="!!message('description')" aria-describedby="description-error"></textarea>
        <small id="description-error" class="error-text">{{ message('description') }}</small>
      </div>

      <div class="field">
        <label for="price">Price (₹, incl. taxes)</label>
        <input id="price" type="number" formControlName="priceRupees" min="0.01" step="0.01" inputmode="decimal" [attr.aria-invalid]="!!message('pricePaise')" aria-describedby="price-error" />
        <small id="price-error" class="error-text">{{ message('pricePaise') }}</small>
      </div>

      <div class="field">
        <label for="initialStock">Opening stock</label>
        <input id="initialStock" type="number" formControlName="initialStock" min="0" step="1" inputmode="numeric" [attr.aria-invalid]="!!message('initialStock')" aria-describedby="initialStock-error" />
        <small id="initialStock-error" class="error-text">{{ message('initialStock') }}</small>
      </div>

      <div class="field check">
        <input id="limitedEdition" type="checkbox" formControlName="limitedEdition" />
        <label for="limitedEdition">Limited edition (each customer can buy it once)</label>
      </div>

      @if (value().limitedEdition) {
        <div class="field">
          <label for="maxPerCustomer">Maximum per customer</label>
          <input id="maxPerCustomer" type="number" formControlName="maxPerCustomer" min="1" max="10" step="1" inputmode="numeric" [attr.aria-invalid]="!!message('maxPerCustomer')" aria-describedby="maxPerCustomer-error" />
          <small id="maxPerCustomer-error" class="error-text">{{ message('maxPerCustomer') }}</small>
        </div>
      }

      <div>
        <button type="submit" [disabled]="saving()">{{ saving() ? 'Adding…' : 'Add product' }}</button>
      </div>
    </form>
  `,
})
export class ProductForm {
  private readonly api = inject(ShopApi);
  private readonly announcer = inject(Announcer);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly types = PRODUCT_TYPES;
  protected readonly franchises = toSignal(this.api.listFranchises().pipe(catchError(() => of([]))), { initialValue: [] });

  protected readonly form = this.fb.group({
    franchiseId: '',
    name: '',
    id: '',
    type: '',
    description: '',
    priceRupees: this.fb.control<number | null>(null),
    initialStock: this.fb.control<number | null>(0),
    limitedEdition: false,
    maxPerCustomer: this.fb.control<number | null>(1),
  });

  protected readonly value = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly created = signal<Product | null>(null);
  protected readonly sent = signal<NewProduct | null>(null);

  /** Checked live once the first submit has been tried, so messages disappear as the fields are fixed. */
  private readonly clientErrors = computed(() => (this.submitted() ? validateProduct(this.value()) : {}));

  /** Field problems are shown next to their fields, so the banner only gives the summary. */
  protected readonly bannerError = computed(() => {
    const e = this.error();
    return e?.code === 'validation-failed' ? new ApiError(e.status, e.code, 'Some fields need attention. See the messages below each field.') : e;
  });

  constructor() {
    // Keep suggesting an id until the admin types their own.
    this.form.valueChanges.subscribe(() => {
      const id = this.form.controls.id;
      if (!id.dirty) {
        id.setValue(suggestProductId(this.form.controls.franchiseId.value, this.form.controls.name.value), { emitEvent: false });
      }
    });
  }

  protected message(field: string): string {
    return this.clientErrors()[field] ?? this.error()?.fieldErrors[field]?.[0] ?? '';
  }

  protected async submit(): Promise<void> {
    if (this.saving()) {
      return;
    }

    this.submitted.set(true);
    this.created.set(null);
    const value = this.form.getRawValue();
    const problems = Object.keys(validateProduct(value));
    if (problems.length > 0) {
      this.error.set(null);
      this.focus(problems[0]);
      return;
    }

    const product = toNewProduct(value);
    this.sent.set(product);
    this.saving.set(true);
    this.error.set(null);

    try {
      const saved = await firstValueFrom(this.api.createProduct(product));
      this.created.set(saved);
      this.announcer.announce(`${saved.name} was added.`);
      // Ready for the next product of the same franchise.
      this.form.reset({ franchiseId: product.franchiseId });
      this.submitted.set(false);
    } catch (error) {
      const apiError = toApiError(error);
      this.error.set(apiError);
      const fields = Object.keys(apiError.fieldErrors);
      if (fields.length > 0) {
        this.focus(fields[0]);
      }
    } finally {
      this.saving.set(false);
    }
  }

  private focus(field: string): void {
    // After the messages render, put the keyboard on the first field that needs fixing.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`#${INPUT_IDS[field] ?? field}`)?.focus());
  }
}

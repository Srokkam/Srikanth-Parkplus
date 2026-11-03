import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { NewProduct, Product } from '../../core/models';
import { ProductForm, ProductFormValue, suggestProductId, validateProduct } from './product-form';

const valid: ProductFormValue = {
  franchiseId: 'csk',
  name: 'CSK Cap',
  id: 'csk-cap',
  type: 'cap',
  description: '',
  priceRupees: 499,
  initialStock: 100,
  limitedEdition: false,
  maxPerCustomer: 1,
};

describe('suggestProductId', () => {
  it.each([
    ['csk', 'CSK Cap', 'csk-cap'],
    ['mi', 'Home Jersey', 'mi-home-jersey'],
    ['kkr', '  Signed  Photo! ', 'kkr-signed-photo'],
    ['', 'Cap', 'cap'],
    ['csk', '', ''],
  ])('franchise %j and name %j suggest %j', (franchise, name, expected) => {
    expect(suggestProductId(franchise, name)).toBe(expected);
  });
});

describe('validateProduct (the API rules, checked before sending)', () => {
  it('accepts a complete product', () => {
    expect(validateProduct(valid)).toEqual({});
  });

  it('reports every problem under the field names the API uses', () => {
    const errors = validateProduct({ ...valid, franchiseId: 'CSK', id: '', name: ' ', type: '', priceRupees: 0, initialStock: -1 });
    expect(Object.keys(errors).sort()).toEqual(['franchiseId', 'id', 'initialStock', 'name', 'pricePaise', 'type']);
  });

  it('needs the id to start with the franchise code', () => {
    expect(validateProduct({ ...valid, id: 'mi-cap' })['id']).toBe('Start the id with "csk-".');
  });

  it('needs a maximum of 1 to 10 per customer for a limited edition only', () => {
    expect(validateProduct({ ...valid, limitedEdition: true, maxPerCustomer: 11 })['maxPerCustomer']).toBeDefined();
    expect(validateProduct({ ...valid, limitedEdition: false, maxPerCustomer: null })).toEqual({});
  });
});

describe('ProductForm', () => {
  function configure(createProduct: ShopApi['createProduct']) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'products/:franchiseId/:productId', children: [] }]),
        { provide: ShopApi, useValue: { listFranchises: () => of([{ code: 'csk' }, { code: 'kkr' }]), createProduct } },
      ],
    });
  }

  async function render() {
    const fixture = TestBed.createComponent(ProductForm);
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const type = async (selector: string, value: string) => {
      const input = el.querySelector(selector) as HTMLInputElement | HTMLSelectElement;
      input.value = value;
      input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
      fixture.detectChanges();
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await new Promise((resolve) => setTimeout(resolve, 5));
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const text = (selector: string) => el.querySelector(selector)?.textContent?.trim() ?? '';

    return { fixture, el, type, submit, text };
  }

  it('suggests the product id from the franchise and name until the admin types one', async () => {
    configure(vi.fn());
    const { el, type } = await render();
    const id = el.querySelector('#productId') as HTMLInputElement;

    await type('#franchiseId', 'csk');
    await type('#name', 'CSK Cap');
    expect(id.value).toBe('csk-cap');

    await type('#productId', 'csk-cap-2026');
    await type('#name', 'CSK Winter Cap');
    expect(id.value).toBe('csk-cap-2026');
  });

  it('checks the fields before calling the API and shows each problem next to its field', async () => {
    const createProduct = vi.fn();
    configure(createProduct);
    const { submit, text, el } = await render();

    await submit();

    expect(createProduct).not.toHaveBeenCalled();
    expect(text('#name-error')).toBe('Enter a name.');
    expect(text('#price-error')).toBe('Enter a price above ₹0.');
    expect(el.querySelector('#name')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('sends the price in paise with the limited-edition settings and links to the new product', async () => {
    const createProduct = vi.fn((p: NewProduct) => of<Product>({ ...p, available: p.initialStock, stockStatus: 'low-stock' }));
    configure(createProduct);
    const { el, type, submit, text } = await render();

    await type('#franchiseId', 'kkr');
    await type('#name', 'KKR Signed Photo');
    await type('#type', 'signed-photo');
    await type('#price', '4999.5');
    await type('#initialStock', '2');
    (el.querySelector('#limitedEdition') as HTMLInputElement).click();
    await submit();

    expect(createProduct).toHaveBeenCalledWith({
      franchiseId: 'kkr',
      id: 'kkr-signed-photo',
      type: 'signed-photo',
      name: 'KKR Signed Photo',
      description: '',
      pricePaise: 499950,
      limitedEdition: true,
      maxPerCustomer: 1,
      initialStock: 2,
    });
    expect(text('[role=status]')).toContain('KKR Signed Photo was added.');
    expect(el.querySelector('a[href="/products/kkr/kkr-signed-photo"]')).not.toBeNull();
  });

  it("shows the API's field errors next to their fields", async () => {
    const rejected = new ApiError(400, 'validation-failed', 'One or more fields are invalid.', {}, { name: ['Name must be 1 to 80 characters.'] });
    configure(() => throwError(() => rejected));
    const { type, submit, text } = await render();

    await type('#franchiseId', 'csk');
    await type('#name', 'CSK Cap');
    await type('#type', 'cap');
    await type('#price', '499');
    await submit();

    expect(text('#name-error')).toBe('Name must be 1 to 80 characters.');
    expect(text('[role=alert]')).toContain('Some fields need attention');
  });

  it('explains that an id is taken and links to the product that has it', async () => {
    const taken = new ApiError(409, 'product-exists', "A product with id 'csk-cap' already exists in franchise 'csk'.");
    configure(() => throwError(() => taken));
    const { el, type, submit, text } = await render();

    await type('#franchiseId', 'csk');
    await type('#name', 'CSK Cap');
    await type('#type', 'cap');
    await type('#price', '499');
    await submit();

    expect(text('[role=alert]')).toContain('already exists');
    expect(el.querySelector('a[href="/products/csk/csk-cap"]')).not.toBeNull();
  });
});

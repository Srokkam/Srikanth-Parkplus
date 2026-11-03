import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShopApi } from '../../core/api/shop-api';
import { Order, Page } from '../../core/models';
import { OrdersPage } from './orders-page';

const order = (id: string, createdUtc: string, itemCount?: number): Order => ({
  id,
  userId: 'dev-customer',
  createdUtc,
  lines: [
    { productId: 'csk-cap', franchiseId: 'csk', name: 'CSK Cap', unitPricePaise: 49900, quantity: 3, lineTotalPaise: 149700 },
    { productId: 'mi-jersey', franchiseId: 'mi', name: 'MI Jersey', unitPricePaise: 199900, quantity: 2, lineTotalPaise: 399800 },
  ],
  shippingPaise: 0,
  subtotalPaise: 549500,
  totalPaise: 549500,
  itemCount,
});

describe('OrdersPage', () => {
  function configure(orders: Order[]) {
    const listOrders = vi.fn(() => of<Page<Order>>({ items: orders, continuationToken: null, totalCount: null }));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: ShopApi, useValue: { listOrders } }],
    });
    localStorage.clear();
    return listOrders;
  }

  async function render() {
    const fixture = TestBed.createComponent(OrdersPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('lists orders newest first with the time in IST by default and the item count', async () => {
    configure([order('o-2', '2026-10-28T10:15:00.000Z', 5), order('o-1', '2026-10-27T04:00:00.000Z')]);
    const { el } = await render();

    const rows = Array.from(el.querySelectorAll('tbody tr'));
    expect(rows[0].textContent).toContain('28 Oct 2026');
    expect(rows[0].textContent).toContain('IST');
    expect(rows[0].textContent).toContain('₹5,495.00');
    expect(rows[0].querySelectorAll('td')[0].textContent?.trim()).toBe('5');
    // An older API without itemCount: the page sums the lines instead.
    expect(rows[1].querySelectorAll('td')[0].textContent?.trim()).toBe('5');
  });

  it('asks the API for whole IST days: from the start of "from" up to the start of the day after "to"', async () => {
    const listOrders = configure([]);
    const { fixture, el } = await render();

    (el.querySelector('#from') as HTMLInputElement).value = '2026-10-01';
    (el.querySelector('#to') as HTMLInputElement).value = '2026-10-31';
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(listOrders).toHaveBeenLastCalledWith({
      pageSize: 10,
      continuationToken: null,
      from: '2026-09-30T18:30:00.000Z',
      to: '2026-10-31T18:30:00.000Z',
    });
    expect(el.textContent).toContain('No orders between these dates.');
  });

  it('refuses a range that ends before it starts, without calling the API', async () => {
    const listOrders = configure([]);
    const { fixture, el } = await render();
    listOrders.mockClear();

    (el.querySelector('#from') as HTMLInputElement).value = '2026-10-31';
    (el.querySelector('#to') as HTMLInputElement).value = '2026-10-01';
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(listOrders).not.toHaveBeenCalled();
    expect(el.querySelector('[role=alert]')?.textContent).toContain('"From" must be on or before "To"');
    expect(el.querySelector('#from')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('labels every filter control', async () => {
    configure([]);
    const { el } = await render();

    for (const control of Array.from(el.querySelectorAll('form input, form select'))) {
      expect(el.querySelector(`label[for="${control.id}"]`), `label for #${control.id}`).not.toBeNull();
    }
  });
});

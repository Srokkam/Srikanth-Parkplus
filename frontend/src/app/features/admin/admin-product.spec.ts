import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import { ShopApi } from '../../core/api/shop-api';
import { AdminProduct, AuditEntry, Page } from '../../core/models';
import { AdminProductPage } from './admin-product';

const cap = (pricePaise: number, priceETag: string): AdminProduct => ({
  id: 'csk-cap',
  franchiseId: 'csk',
  name: 'CSK Cap',
  pricePaise,
  available: 97,
  priceETag,
  stockETag: '"s1"',
});

const history = (entries: AuditEntry[] = []): Page<AuditEntry> => ({ items: entries, continuationToken: null, totalCount: null });

describe('AdminProductPage (optimistic concurrency and the audit trail)', () => {
  function configure(api: Partial<ShopApi>) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ShopApi, useValue: { listAudit: () => of(history()), ...api } },
      ],
    });
  }

  async function render() {
    const fixture = TestBed.createComponent(AdminProductPage);
    fixture.componentRef.setInput('franchiseId', 'csk');
    fixture.componentRef.setInput('productId', 'csk-cap');
    fixture.detectChanges();
    await settle(fixture);
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    for (let i = 0; i < 3; i++) {
      await new Promise((resolve) => setTimeout(resolve, 5));
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  async function submitPrice(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }, el: HTMLElement, rupees: string, reason: string) {
    (el.querySelector('#price') as HTMLInputElement).value = rupees;
    (el.querySelector('#priceReason') as HTMLInputElement).value = reason;
    el.querySelectorAll('form')[0].dispatchEvent(new Event('submit'));
    await settle(fixture);
  }

  it('sends the new price in paise with the version it read, then shows the change in the history', async () => {
    const raised: AuditEntry = { id: 'a1', field: 'price', oldValue: 49900, newValue: 54900, delta: null, reason: 'price review', actor: 'dev-admin', tsUtc: '2026-11-03T05:00:00.000Z' };
    const getAdminProduct = vi.fn().mockReturnValueOnce(of(cap(49900, '"p1"'))).mockReturnValue(of(cap(54900, '"p2"')));
    const changePrice = vi.fn(() => of(undefined));
    const listAudit = vi.fn((_f: string, _p: string, field: string) => of(history(field === 'price' ? [raised] : [])));
    configure({ getAdminProduct, changePrice, listAudit });
    const { fixture, el } = await render();
    listAudit.mockClear();

    await submitPrice(fixture, el, '549', 'price review');

    expect(changePrice).toHaveBeenCalledWith('csk', 'csk-cap', 54900, 'price review', '"p1"');
    expect(el.querySelector('[role=status]')?.textContent).toContain('Price changed to ₹549.00.');
    expect(el.textContent).toContain('₹499.00 → ₹549.00');
    expect(el.textContent).toContain('dev-admin');
  });

  it('on a 412 says someone else changed it, saves nothing, shows the latest values and keeps what was typed', async () => {
    const getAdminProduct = vi.fn().mockReturnValueOnce(of(cap(49900, '"p1"'))).mockReturnValue(of(cap(59900, '"p9"')));
    const changePrice = vi.fn(() => throwError(() => new ApiError(412, 'precondition-failed', 'Someone changed this since you loaded it.')));
    configure({ getAdminProduct, changePrice });
    const { fixture, el } = await render();

    await submitPrice(fixture, el, '549', 'price review');

    expect(el.querySelector('[role=alert]')?.textContent).toContain('Someone else changed this product');
    expect(el.textContent).toContain('Current price ₹599.00');
    expect((el.querySelector('#price') as HTMLInputElement).value).toBe('549');
    expect(el.querySelector('[role=status]')).toBeNull();
  });

  it('asks for a reason before sending anything, because every change is audited', async () => {
    const changePrice = vi.fn();
    configure({ getAdminProduct: () => of(cap(49900, '"p1"')), changePrice });
    const { fixture, el } = await render();

    await submitPrice(fixture, el, '549', '  ');

    expect(changePrice).not.toHaveBeenCalled();
    expect(el.querySelector('[role=alert]')?.textContent).toContain('Give a reason');
  });

  it('labels every input', async () => {
    configure({ getAdminProduct: () => of(cap(49900, '"p1"')) });
    const { el } = await render();

    for (const input of Array.from(el.querySelectorAll('input'))) {
      expect(el.querySelector(`label[for="${input.id}"]`), `label for #${input.id}`).not.toBeNull();
    }
  });
});
